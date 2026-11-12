using System.Collections.Concurrent;
using System.Diagnostics;
using FFmpeg.AutoGen;

namespace ShackRemote.Core.Media;

/// <summary>
/// Client: Dekodieren und Anzeigen komplett auf der GPU.
/// H.264 -> D3D11VA-Decoder (Bild bleibt als NV12-Textur auf der GPU) -> D3D11 Video Processor
/// (NV12 -> BGRA und Skalierung in einem Schritt) -> Swapchain des Anzeigefensters.
/// Ein eigener Thread dekodiert und zeigt an; die Netzwerk-Leseschleife legt nur Pakete ab.
/// </summary>
public sealed unsafe class VideoRenderer : IDisposable
{
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
    private const uint DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
    private const int MaxQueuedPackets = 20;   // ~1/3 s bei 60 fps; mehr = Rückstand

    private readonly IntPtr _hwnd;
    private readonly BlockingCollection<(byte[] Data, bool Key)> _queue = new(new ConcurrentQueue<(byte[], bool)>());
    private readonly Thread _thread;
    private volatile bool _stop;

    // Zielgröße (aus dem UI-Thread gesetzt)
    private int _targetWidth, _targetHeight;
    private int _sizeChanged = 1;

    // FFmpeg
    private AVBufferRef* _device;
    private AVCodecContext* _dec;
    private AVFrame* _frame;
    private AVFrame* _last;          // zuletzt angezeigtes Bild, für Neuzeichnen bei Größenänderung
    private AVPacket* _packet;
    private AVCodecContext_get_format? _getFormat;   // Verweis halten

    // D3D11 (alle Zeiger stammen aus dem FFmpeg-Gerät)
    private AVD3D11VADeviceContext* _hw;
    private IntPtr _swapChain, _backBuffer, _outputView, _enumerator, _processor;
    private readonly Dictionary<(IntPtr, int), IntPtr> _inputViews = new();
    private int _vpInWidth, _vpInHeight, _vpOutWidth, _vpOutHeight;
    private int _bufWidth, _bufHeight;

    private bool _waitForKey = true;
    private long _lastKeyRequest;

    /// <summary>Meldungen für das Protokoll.</summary>
    public event Action<string>? Log;
    /// <summary>Decoder braucht ein Schlüsselbild (Einstieg, Rückstand, Fehler).</summary>
    public event Action? KeyframeNeeded;

    /// <summary>Zähler für die Statuszeile: dekodierte und angezeigte Bilder, Neusynchronisationen.</summary>
    public int FramesDecoded;
    public int FramesShown;
    public int Resyncs;

    /// <summary>Größe des Bildes beim Server.</summary>
    public int SourceWidth { get; private set; }
    /// <summary>Höhe des Bildes beim Server.</summary>
    public int SourceHeight { get; private set; }

    /// <summary>Wo das Bild im Fenster liegt (Umrechnung der Mauskoordinaten).</summary>
    public Rectangle ImageBounds { get; private set; }

    /// <summary>true, sobald mindestens ein Bild angezeigt wurde.</summary>
    public bool HasPicture { get; private set; }

    public VideoRenderer(IntPtr hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _targetWidth = Math.Max(16, width);
        _targetHeight = Math.Max(16, height);
        _thread = new Thread(Run) { IsBackground = true, Name = "Decoder/Anzeige", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Decoder-/Anzeige-Thread starten.</summary>
    public void Start()
    {
        FfmpegLib.EnsureLoaded();
        _thread.Start();
    }

    /// <summary>Aus der Netzwerk-Leseschleife: Paket ablegen, nie blockieren.</summary>
    public void Submit(byte[] data, bool key)
    {
        if (_stop) return;
        _queue.Add((data, key));
    }

    /// <summary>Neuer Datenstrom (Monitorwechsel): bis zum nächsten Schlüsselbild nichts dekodieren.</summary>
    public void Reset()
    {
        while (_queue.TryTake(out _)) { }
        _queue.Add((Array.Empty<byte>(), false));   // Marker: Decoder leeren
    }

    /// <summary>Neue Fenstergröße; Swapchain und Videoprozessor werden im Thread angepasst.</summary>
    public void Resize(int width, int height)
    {
        _targetWidth = Math.Max(16, width);
        _targetHeight = Math.Max(16, height);
        Interlocked.Exchange(ref _sizeChanged, 1);
        _queue.Add((Array.Empty<byte>(), true));    // Marker: neu zeichnen
    }

    // ------------------------------------------------------------------------------------------

    private void Run()
    {
        try
        {
            InitDecoder();
            var keyWait = Stopwatch.StartNew();

            while (!_stop)
            {
                if (!_queue.TryTake(out var item, 250))
                {
                    if (_waitForKey && keyWait.Elapsed > TimeSpan.FromSeconds(1)) RequestKey();
                    continue;
                }

                if (item.Data.Length == 0)
                {
                    if (item.Key) Redraw();                  // Größenänderung
                    else { ffmpeg.avcodec_flush_buffers(_dec); _waitForKey = true; keyWait.Restart(); }
                    continue;
                }

                // Rückstand? Dann alles verwerfen und sauber beim nächsten Schlüsselbild weitermachen.
                if (_queue.Count > MaxQueuedPackets)
                {
                    int dropped = 0;
                    while (_queue.TryTake(out var old))
                    {
                        if (old.Data.Length > 0 && old.Key) { item = old; dropped = -1; break; }
                        dropped++;
                    }
                    if (dropped >= 0)
                    {
                        Interlocked.Increment(ref Resyncs);
                        Log?.Invoke($"Rückstand: {dropped + 1} Pakete verworfen, warte auf Schlüsselbild");
                        _waitForKey = true;
                        keyWait.Restart();
                        RequestKey();
                        continue;
                    }
                }

                if (_waitForKey)
                {
                    if (!item.Key)
                    {
                        if (keyWait.Elapsed > TimeSpan.FromSeconds(1)) RequestKey();
                        continue;
                    }
                    _waitForKey = false;
                }

                Decode(item.Data);
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Log?.Invoke($"Anzeige gestoppt: {ex.Message}");
        }
        finally
        {
            Cleanup();
        }
    }

    private void RequestKey()
    {
        long now = Environment.TickCount64;
        if (now - _lastKeyRequest < 1000) return;
        _lastKeyRequest = now;
        KeyframeNeeded?.Invoke();
    }

    private void InitDecoder()
    {
        AVBufferRef* device = null;
        FfmpegLib.Check(ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "D3D11-Gerät");
        _device = device;
        _hw = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)_device->data)->hwctx;

        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        _dec = ffmpeg.avcodec_alloc_context3(codec);
        _dec->hw_device_ctx = ffmpeg.av_buffer_ref(_device);
        _dec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        _dec->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
        _dec->thread_count = 1;                     // keine Bildverzögerung durch Frame-Threads
        _dec->extra_hw_frames = 2;                  // ein Bild halten wir zum Neuzeichnen fest
        _getFormat = (ctx, formats) =>
        {
            for (var p = formats; *p != AVPixelFormat.AV_PIX_FMT_NONE; p++)
                if (*p == AVPixelFormat.AV_PIX_FMT_D3D11) return *p;
            Log?.Invoke("Grafikkarte kann diesen Datenstrom nicht dekodieren");
            return AVPixelFormat.AV_PIX_FMT_NONE;
        };
        _dec->get_format = _getFormat;
        FfmpegLib.Check(ffmpeg.avcodec_open2(_dec, codec, null), "H.264-Decoder öffnen");

        _frame = ffmpeg.av_frame_alloc();
        _last = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();
        Log?.Invoke("Decoder: H.264 über D3D11VA (GPU)");
    }

    private void Decode(byte[] data)
    {
        fixed (byte* p = data)
        {
            _packet->data = p;
            _packet->size = data.Length;
            int ret = ffmpeg.avcodec_send_packet(_dec, _packet);
            _packet->data = null;
            _packet->size = 0;
            if (ret < 0)
            {
                Log?.Invoke($"Dekodierfehler: {FfmpegLib.Error(ret)}");
                _waitForKey = true;
                RequestKey();
                return;
            }
        }

        while (ffmpeg.avcodec_receive_frame(_dec, _frame) >= 0)
        {
            Interlocked.Increment(ref FramesDecoded);
            if (_frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11)
            {
                // Neuere Bilder in der Warteschlange? Dann dieses nur dekodieren, nicht anzeigen.
                if (_queue.Count <= 1 || Volatile.Read(ref _sizeChanged) == 1)
                    Present(_frame);
                ffmpeg.av_frame_unref(_last);
                ffmpeg.av_frame_move_ref(_last, _frame);
            }
            else
            {
                ffmpeg.av_frame_unref(_frame);
            }
        }
    }

    private void Redraw()
    {
        if (_last != null && _last->data[0] != null) Present(_last);
    }

    private void Lock() => ((delegate* unmanaged[Cdecl]<void*, void>)_hw->@lock.Pointer)(_hw->lock_ctx);
    private void Unlock() => ((delegate* unmanaged[Cdecl]<void*, void>)_hw->unlock.Pointer)(_hw->lock_ctx);

    private void Present(AVFrame* frame)
    {
        Lock();
        try
        {
            EnsureSwapChain();
            EnsureProcessor(frame->width, frame->height);
            SourceWidth = frame->width;
            SourceHeight = frame->height;

            var texture = (IntPtr)frame->data[0];
            int slice = (int)(nint)frame->data[1];
            var input = InputView(texture, slice);

            var vc = (IntPtr)_hw->video_context;
            var vtbl = Com.Vtbl(vc);

            // Farbraum aus dem Datenstrom (BT.709/601, begrenzter oder voller Bereich)
            uint cs = 0;
            if (frame->colorspace == AVColorSpace.AVCOL_SPC_BT709) cs |= 1u << 2;            // YCbCr_Matrix
            cs |= (frame->color_range == AVColorRange.AVCOL_RANGE_JPEG ? 2u : 1u) << 4;        // Nominal_Range
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint*, void>)vtbl[28])(vc, _processor, 0, &cs);  // SetStreamColorSpace

            // Quelle: sichtbarer Bereich (Textur kann z. B. 1088 statt 1080 Zeilen haben)
            var src = new RECT(0, 0, frame->width, frame->height);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, RECT*, void>)vtbl[30])(vc, _processor, 0, 1, &src);   // SetStreamSourceRect

            // Ziel: Seitenverhältnis erhalten, mittig
            double scale = Math.Min((double)_bufWidth / frame->width, (double)_bufHeight / frame->height);
            int w = Math.Max(1, (int)Math.Round(frame->width * scale));
            int h = Math.Max(1, (int)Math.Round(frame->height * scale));
            int x = (_bufWidth - w) / 2, y = (_bufHeight - h) / 2;
            var dst = new RECT(x, y, x + w, y + h);
            ImageBounds = new Rectangle(x, y, w, h);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, RECT*, void>)vtbl[31])(vc, _processor, 0, 1, &dst);   // SetStreamDestRect

            var stream = new VpStream { Enable = 1, InputSurface = input };
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, uint, VpStream*, int>)vtbl[53])(vc, _processor, _outputView, 0, 1, &stream); // VideoProcessorBlt
            Com.Check(hr, "VideoProcessorBlt");

            // IDXGISwapChain::Present = 8; ohne Warten auf den Bildaufbau (geringste Verzögerung)
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)Com.Vtbl(_swapChain)[8])(_swapChain, 0, 0);
            Com.Check(hr, "Present");
            Interlocked.Increment(ref FramesShown);
            HasPicture = true;
        }
        finally
        {
            Unlock();
        }
    }

    private void EnsureSwapChain()
    {
        if (_swapChain != IntPtr.Zero && Interlocked.Exchange(ref _sizeChanged, 0) == 0) return;
        int w = _targetWidth, h = _targetHeight;

        if (_swapChain == IntPtr.Zero)
        {
            Interlocked.Exchange(ref _sizeChanged, 0);
            var device = (IntPtr)_hw->device;
            var dxgiDevice = Com.QueryInterface(device, Com.IID_IDXGIDevice);
            IntPtr adapter = IntPtr.Zero, factory = IntPtr.Zero;
            try
            {
                Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Com.Vtbl(dxgiDevice)[7])(dxgiDevice, &adapter), "GetAdapter");
                var iid = Com.IID_IDXGIFactory2;
                Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Com.Vtbl(adapter)[6])(adapter, &iid, &factory), "GetParent(Factory2)");

                var desc = new SwapChainDesc1
                {
                    Width = (uint)w, Height = (uint)h,
                    Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                    SampleCount = 1,
                    BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT,
                    BufferCount = 2,
                    Scaling = 0,                                  // STRETCH
                    SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD,
                };
                IntPtr swap;
                // IDXGIFactory2::CreateSwapChainForHwnd = 15
                Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, SwapChainDesc1*, void*, IntPtr, IntPtr*, int>)Com.Vtbl(factory)[15])(
                    factory, device, _hwnd, &desc, null, IntPtr.Zero, &swap), "CreateSwapChainForHwnd");
                _swapChain = swap;
                // IDXGIFactory::MakeWindowAssociation = 8; Alt+Enter nicht von DXGI behandeln lassen
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int>)Com.Vtbl(factory)[8])(factory, _hwnd, 2);
            }
            finally
            {
                Com.Release(ref factory);
                Com.Release(ref adapter);
                Com.Release(ref dxgiDevice);
            }
        }
        else
        {
            Com.Release(ref _outputView);
            Com.Release(ref _backBuffer);
            // IDXGISwapChain::ResizeBuffers = 13 (Anzahl/Format beibehalten)
            Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, uint, int>)Com.Vtbl(_swapChain)[13])(_swapChain, 0, (uint)w, (uint)h, 0, 0), "ResizeBuffers");
        }

        _bufWidth = w;
        _bufHeight = h;
        var tex = Com.IID_ID3D11Texture2D;
        IntPtr buffer;
        // IDXGISwapChain::GetBuffer = 9
        Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)Com.Vtbl(_swapChain)[9])(_swapChain, 0, &tex, &buffer), "GetBuffer");
        _backBuffer = buffer;
        _vpOutWidth = 0;   // Ausgabeansicht neu anlegen
    }

    private void EnsureProcessor(int inWidth, int inHeight)
    {
        var vd = (IntPtr)_hw->video_device;
        var vdt = Com.Vtbl(vd);

        if (_processor == IntPtr.Zero || inWidth != _vpInWidth || inHeight != _vpInHeight || _bufWidth != _vpOutWidth || _bufHeight != _vpOutHeight)
        {
            ReleaseProcessor();
            var content = new VpContentDesc
            {
                InputFrameFormat = 0,
                InputFrameRate = new DXGI_RATIONAL { Numerator = 60, Denominator = 1 },
                InputWidth = (uint)inWidth, InputHeight = (uint)inHeight,
                OutputFrameRate = new DXGI_RATIONAL { Numerator = 60, Denominator = 1 },
                OutputWidth = (uint)_bufWidth, OutputHeight = (uint)_bufHeight,
                Usage = 2,                                        // OPTIMAL_QUALITY (Schrift soll scharf bleiben)
            };
            IntPtr enumerator, processor;
            Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, VpContentDesc*, IntPtr*, int>)vdt[10])(vd, &content, &enumerator), "CreateVideoProcessorEnumerator");
            _enumerator = enumerator;
            Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr*, int>)vdt[4])(vd, _enumerator, 0, &processor), "CreateVideoProcessor");
            _processor = processor;

            var vc = (IntPtr)_hw->video_context;
            var vct = Com.Vtbl(vc);
            var black = new VideoColorRgba { A = 1 };
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, VideoColorRgba*, void>)vct[14])(vc, _processor, 0, &black);   // SetOutputBackgroundColor (RGB)
            uint outCs = 0;                                                                                                       // RGB, voller Bereich
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, void>)vct[15])(vc, _processor, &outCs);                       // SetOutputColorSpace
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, void>)vct[27])(vc, _processor, 0, 0);                    // SetStreamFrameFormat progressiv
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, void>)vct[37])(vc, _processor, 0, 0);                     // SetStreamAutoProcessingMode aus
            _vpInWidth = inWidth; _vpInHeight = inHeight;
            _vpOutWidth = 0;
        }

        if (_outputView == IntPtr.Zero || _vpOutWidth == 0)
        {
            Com.Release(ref _outputView);
            var od = new VpOutputViewDesc { ViewDimension = 1 };
            IntPtr view;
            Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, VpOutputViewDesc*, IntPtr*, int>)vdt[9])(vd, _backBuffer, _enumerator, &od, &view), "CreateVideoProcessorOutputView");
            _outputView = view;
            _vpOutWidth = _bufWidth; _vpOutHeight = _bufHeight;

            // Ganze Ausgabefläche gehört dem Prozessor (Ränder schwarz)
            var vc = (IntPtr)_hw->video_context;
            var target = new RECT(0, 0, _bufWidth, _bufHeight);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, RECT*, void>)Com.Vtbl(vc)[13])(vc, _processor, 1, &target);   // SetOutputTargetRect
        }
    }

    private IntPtr InputView(IntPtr texture, int slice)
    {
        if (_inputViews.TryGetValue((texture, slice), out var view)) return view;
        var vd = (IntPtr)_hw->video_device;
        var desc = new VpInputViewDesc { FourCC = 0, ViewDimension = 1, MipSlice = 0, ArraySlice = (uint)slice };
        IntPtr v;
        Com.Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, VpInputViewDesc*, IntPtr*, int>)Com.Vtbl(vd)[8])(vd, texture, _enumerator, &desc, &v), "CreateVideoProcessorInputView");
        _inputViews[(texture, slice)] = v;
        return v;
    }

    private void ReleaseProcessor()
    {
        foreach (var key in _inputViews.Keys.ToList())
        {
            var v = _inputViews[key];
            Com.Release(ref v);
        }
        _inputViews.Clear();
        Com.Release(ref _outputView);
        Com.Release(ref _processor);
        Com.Release(ref _enumerator);
    }

    private void Cleanup()
    {
        if (_hw != null)
        {
            Lock();
            try
            {
                ReleaseProcessor();
                Com.Release(ref _backBuffer);
                Com.Release(ref _swapChain);
            }
            finally { Unlock(); }
        }
        fixed (AVFrame** f = &_frame) ffmpeg.av_frame_free(f);
        fixed (AVFrame** f = &_last) ffmpeg.av_frame_free(f);
        fixed (AVPacket** p = &_packet) ffmpeg.av_packet_free(p);
        fixed (AVCodecContext** c = &_dec) ffmpeg.avcodec_free_context(c);
        fixed (AVBufferRef** d = &_device) ffmpeg.av_buffer_unref(d);
        _hw = null;
    }

    /// <summary>Thread beenden und alle D3D11-/FFmpeg-Objekte freigeben.</summary>
    public void Dispose()
    {
        _stop = true;
        _queue.Add((Array.Empty<byte>(), true));
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(3));
    }
}
