using System.Diagnostics;
using FFmpeg.AutoGen;

namespace ShackRemote.Core.Media;

/// <summary>
/// Server: Aufnahme und Kodierung im eigenen Prozess, alles auf der GPU (wie Sunshine/Parsec):
/// ddagrab (Desktop Duplication, D3D11-Textur) -> hwmap nach QSV -> h264_qsv.
///
/// Drei Threads wie bei ffmpeg.exe: Aufnahme -> Kodieren -> Senden.
/// Zwischen Aufnahme und Encoder liegt genau ein Platz: Ist der Encoder noch beschäftigt, ersetzt das neue Bild
/// das ältere (geringste Verzögerung, kein Stau). Fertige Pakete gehen über eine Warteschlange an den Sende-Thread,
/// damit das Warten aufs Netz den Encoder nicht bremst. Schlüsselbilder auf Anforderung statt Encoder-Neustart.
/// </summary>
public sealed unsafe class ScreenEncoder : IDisposable
{
    private readonly MonitorInfo _monitor;
    private readonly int _fps;
    private readonly Thread _captureThread;
    private Thread? _encodeThread;
    private Thread? _sendThread;
    private readonly System.Collections.Concurrent.BlockingCollection<(byte[] Data, bool Key)> _sendQueue = new();
    private volatile bool _stop;
    private int _keyframeRequested = 1;   // erstes Bild immer IDR

    // Übergabeplatz zwischen den Threads
    private readonly object _slotGate = new();
    private readonly AutoResetEvent _slotFilled = new(false);
    private AVFrame* _slot;

    // Statistik
    private int _captured, _encoded, _replaced;
    private long _bytes;
    private int _keys;
    private long _encodeTicks, _sendTicks;
    private int _sent;

    // FFmpeg-Objekte (Aufbau im Aufnahme-Thread)
    private AVBufferRef* _device;
    private AVFilterGraph* _graph;
    private AVFilterContext* _sink;
    private AVCodecContext* _enc;
    private AVBSFContext* _bsf;

    /// <summary>Kodiertes Paket (Annex B, SPS/PPS vor jedem Schlüsselbild) und ob es ein Schlüsselbild ist.</summary>
    public event Action<byte[], bool>? PacketReady;
    public event Action<int, int>? Started;      // Breite, Höhe
    public event Action<string>? Log;
    public event Action<string>? Failed;

    public ScreenEncoder(MonitorInfo monitor, int fps)
    {
        _monitor = monitor;
        _fps = fps;
        _captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = $"Aufnahme Monitor {monitor.Index}", Priority = ThreadPriority.AboveNormal };
    }

    public void Start()
    {
        FfmpegLib.EnsureLoaded();
        _captureThread.Start();
    }

    public void RequestKeyframe() => Interlocked.Exchange(ref _keyframeRequested, 1);

    // ---- Aufnahme-Thread ------------------------------------------------------------------------

    private void CaptureLoop()
    {
        try
        {
            // Im Dienstbetrieb (SYSTEM) auch den Sperr-/Anmeldebildschirm aufnehmen: Thread an den Eingabe-Desktop binden.
            var desktop = Desktop.AttachToInputDesktop() ?? Desktop.ThreadDesktopName();
            if (!Desktop.ThreadIsOnInputDesktop())
                throw new InvalidOperationException(
                    $"Anzeige nicht zugänglich (\"{Desktop.InputDesktopName() ?? "gesperrt"}\") – ohne Dienst kein Sperrbildschirm");
            Log?.Invoke($"Video: Desktop \"{desktop}\"" + (Desktop.IsSystem ? " (Dienstbetrieb)" : ""));
            Open();
            _encodeThread = new Thread(EncodeLoop) { IsBackground = true, Name = $"Encoder Monitor {_monitor.Index}", Priority = ThreadPriority.AboveNormal };
            _encodeThread.Start();
            _sendThread = new Thread(SendLoop) { IsBackground = true, Name = $"Senden Monitor {_monitor.Index}" };
            _sendThread.Start();

            var report = Stopwatch.StartNew();
            var desktopCheck = Stopwatch.StartNew();
            while (!_stop)
            {
                // Wechsel normaler Desktop <-> Sperr-/Anmeldebildschirm: Aufnahme neu aufbauen lassen
                // (meldet DXGI nicht immer selbst als Fehler).
                if (desktopCheck.ElapsedMilliseconds >= 250)
                {
                    desktopCheck.Restart();
                    if (!Desktop.ThreadIsOnInputDesktop())
                        throw new InvalidOperationException(
                            $"Anzeige gewechselt (jetzt \"{Desktop.InputDesktopName() ?? "gesperrt"}\")");
                }

                var frame = ffmpeg.av_frame_alloc();
                int ret = ffmpeg.av_buffersink_get_frame(_sink, frame);
                if (ret < 0)
                {
                    ffmpeg.av_frame_free(&frame);
                    if (ret == FfmpegLib.AVERROR(ffmpeg.EAGAIN)) continue;
                    FfmpegLib.Check(ret, "Aufnahme");
                }
                Interlocked.Increment(ref _captured);

                lock (_slotGate)
                {
                    if (_slot != null)
                    {
                        // Encoder hat das vorige Bild noch nicht abgeholt: durch das neuere ersetzen
                        var old = _slot;
                        ffmpeg.av_frame_free(&old);
                        _replaced++;
                    }
                    _slot = frame;
                }
                _slotFilled.Set();

                if (report.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    double s = report.Elapsed.TotalSeconds;
                    int captured = Interlocked.Exchange(ref _captured, 0);
                    int encoded = Interlocked.Exchange(ref _encoded, 0);
                    long bytes = Interlocked.Exchange(ref _bytes, 0);
                    int keys = Interlocked.Exchange(ref _keys, 0);
                    int replaced;
                    lock (_slotGate) { replaced = _replaced; _replaced = 0; }
                    double encMs = encoded > 0 ? Stopwatch.GetElapsedTime(0, Interlocked.Exchange(ref _encodeTicks, 0)).TotalMilliseconds / encoded : 0;
                    int sent = Interlocked.Exchange(ref _sent, 0);
                    double sendMs = sent > 0 ? Stopwatch.GetElapsedTime(0, Interlocked.Exchange(ref _sendTicks, 0)).TotalMilliseconds / sent : 0;
                    Log?.Invoke($"Video: aufgenommen {captured / s:F0} fps, kodiert {encoded / s:F0} fps, " +
                                $"{bytes * 8 / 1000 / s:F0} kbit/s, {keys} Schlüsselbilder" +
                                (replaced > 0 ? $", {replaced} Bilder übersprungen" : "") +
                                $" | je Bild: Kodieren {encMs:F1} ms, Senden {sendMs:F1} ms, Warteschlange {_sendQueue.Count}");
                    report.Restart();
                }
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Failed?.Invoke(ex.Message);
        }
        finally
        {
            _stop = true;
            _slotFilled.Set();
            if (_encodeThread is { IsAlive: true }) _encodeThread.Join(TimeSpan.FromSeconds(3));
            _sendQueue.CompleteAdding();
            if (_sendThread is { IsAlive: true }) _sendThread.Join(TimeSpan.FromSeconds(3));
            lock (_slotGate)
            {
                if (_slot != null) { var f = _slot; ffmpeg.av_frame_free(&f); _slot = null; }
            }
            Close();
        }
    }

    /// <summary>Welcher H.264-Encoder benutzt wird.</summary>
    public enum EncoderKind { IntelQuickSync, NvidiaNvenc, AmdAmf, Software }

    /// <summary>Reihenfolge nach Hersteller der Grafikkarte; Software (libx264, Prozessor) immer als letzte Rettung.</summary>
    private static EncoderKind[] Candidates(uint vendor) => vendor switch
    {
        0x8086 => new[] { EncoderKind.IntelQuickSync, EncoderKind.Software },
        0x10DE => new[] { EncoderKind.NvidiaNvenc, EncoderKind.Software },
        0x1002 => new[] { EncoderKind.AmdAmf, EncoderKind.Software },
        // Hersteller unbekannt: alles der Reihe nach probieren
        _ => new[] { EncoderKind.IntelQuickSync, EncoderKind.NvidiaNvenc, EncoderKind.AmdAmf, EncoderKind.Software },
    };

    public static string Describe(EncoderKind kind) => kind switch
    {
        EncoderKind.IntelQuickSync => "Intel Quick Sync (h264_qsv)",
        EncoderKind.NvidiaNvenc => "NVIDIA NVENC (h264_nvenc)",
        EncoderKind.AmdAmf => "AMD AMF (h264_amf)",
        _ => "Prozessor (libx264)",
    };

    /// <summary>Gewählter Encoder (nach dem Start).</summary>
    public EncoderKind Kind { get; private set; }

    /// <summary>Je Grafikkarte der zuletzt funktionierende Encoder – wird beim nächsten Mal zuerst probiert.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, EncoderKind> Working = new();

    private void Open()
    {
        var order = Candidates(_monitor.Vendor).ToList();
        if (Working.TryGetValue(_monitor.Adapter, out var known) && order.Remove(known)) order.Insert(0, known);

        Exception? last = null;
        foreach (var kind in order)
        {
            try
            {
                OpenWith(kind);
                Working[_monitor.Adapter] = kind;
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Close();
                // Liegt es am gesperrten/gewechselten Bildschirm, hilft ein anderer Encoder nicht
                if (!Desktop.ThreadIsOnInputDesktop()) throw;
                Log?.Invoke($"Video: {Describe(kind)} nicht möglich ({ex.Message}) – versuche nächsten Encoder");
            }
        }
        throw last ?? new InvalidOperationException("Kein Encoder verfügbar.");
    }

    private void OpenWith(EncoderKind kind)
    {
        // --- D3D11-Gerät auf der Grafikkarte, an der der Monitor hängt (Adapter-Index wie bei DXGI) ---
        AVBufferRef* device = null;
        FfmpegLib.Check(ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA,
            _monitor.Adapter.ToString(), null, 0), $"D3D11-Gerät auf Grafikkarte {_monitor.Adapter}");
        _device = device;

        // --- Filtergraph: ddagrab -> (je Encoder) -> buffersink ---
        _graph = ffmpeg.avfilter_graph_alloc();
        AVFilterContext* sink;
        FfmpegLib.Check(ffmpeg.avfilter_graph_create_filter(&sink, ffmpeg.avfilter_get_by_name("buffersink"), "out", null, null, _graph), "buffersink");
        _sink = sink;

        var grab = $"ddagrab=output_idx={_monitor.Output}:framerate={_fps}:draw_mouse=1";
        var description = kind switch
        {
            EncoderKind.IntelQuickSync => grab + ",hwmap=derive_device=qsv,format=qsv",
            EncoderKind.NvidiaNvenc or EncoderKind.AmdAmf => grab,                     // D3D11-Textur (BGRA) direkt in den Encoder
            _ => grab + ",hwdownload,format=bgra,format=yuv420p",                       // in den Hauptspeicher, für libx264
        };

        AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();
        inputs->name = ffmpeg.av_strdup("out");
        inputs->filter_ctx = sink;
        inputs->pad_idx = 0;
        inputs->next = null;
        AVFilterInOut* outputs = null;
        int ret = ffmpeg.avfilter_graph_parse_ptr(_graph, description, &inputs, &outputs, null);
        ffmpeg.avfilter_inout_free(&inputs);
        ffmpeg.avfilter_inout_free(&outputs);
        FfmpegLib.Check(ret, "Filtergraph");

        // Gerät allen Filtern geben (ddagrab nutzt es beim Konfigurieren)
        for (uint i = 0; i < _graph->nb_filters; i++)
        {
            var f = _graph->filters[i];
            if (f->hw_device_ctx == null) f->hw_device_ctx = ffmpeg.av_buffer_ref(_device);
        }
        FfmpegLib.Check(ffmpeg.avfilter_graph_config(_graph, null), $"Monitor {_monitor.Display} öffnen");

        int width = ffmpeg.av_buffersink_get_w(sink);
        int height = ffmpeg.av_buffersink_get_h(sink);
        var timeBase = ffmpeg.av_buffersink_get_time_base(sink);

        // --- Encoder ---
        var name = kind switch
        {
            EncoderKind.IntelQuickSync => "h264_qsv",
            EncoderKind.NvidiaNvenc => "h264_nvenc",
            EncoderKind.AmdAmf => "h264_amf",
            _ => "libx264",
        };
        var codec = ffmpeg.avcodec_find_encoder_by_name(name);
        if (codec == null) throw new InvalidOperationException($"{name} fehlt in dieser FFmpeg-Version.");
        var enc = ffmpeg.avcodec_alloc_context3(codec);
        _enc = enc;
        enc->width = width;
        enc->height = height;
        enc->time_base = timeBase;
        enc->framerate = new AVRational { num = _fps, den = 1 };
        enc->gop_size = _fps * 2;             // regulär alle 2 s, sonst auf Anforderung
        enc->max_b_frames = 0;                // keine B-Bilder = keine Umsortier-Verzögerung
        enc->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
        static void Opt(void* priv, string key, string value) => ffmpeg.av_opt_set(priv, key, value, 0);

        switch (kind)
        {
            case EncoderKind.IntelQuickSync:
                enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_QSV;
                enc->hw_frames_ctx = ffmpeg.av_buffer_ref(ffmpeg.av_buffersink_get_hw_frames_ctx(sink));
                enc->global_quality = 20;     // ICQ wie in Stufe 0
                Opt(enc->priv_data, "async_depth", "1");
                Opt(enc->priv_data, "forced_idr", "1");
                break;
            case EncoderKind.NvidiaNvenc:
                enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
                enc->hw_frames_ctx = ffmpeg.av_buffer_ref(ffmpeg.av_buffersink_get_hw_frames_ctx(sink));
                enc->bit_rate = 0;
                Opt(enc->priv_data, "preset", "p4");
                Opt(enc->priv_data, "tune", "ull");           // ultra low latency
                Opt(enc->priv_data, "rc", "vbr");
                Opt(enc->priv_data, "cq", "23");
                Opt(enc->priv_data, "zerolatency", "1");
                Opt(enc->priv_data, "delay", "0");
                Opt(enc->priv_data, "forced-idr", "1");
                break;
            case EncoderKind.AmdAmf:
                enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
                enc->hw_frames_ctx = ffmpeg.av_buffer_ref(ffmpeg.av_buffersink_get_hw_frames_ctx(sink));
                Opt(enc->priv_data, "usage", "ultralowlatency");
                Opt(enc->priv_data, "quality", "speed");
                Opt(enc->priv_data, "rc", "cqp");
                Opt(enc->priv_data, "qp_i", "22");
                Opt(enc->priv_data, "qp_p", "24");
                Opt(enc->priv_data, "forced_idr", "1");
                break;
            default:
                enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                enc->thread_count = 0;        // automatisch
                Opt(enc->priv_data, "preset", "superfast");
                Opt(enc->priv_data, "tune", "zerolatency");
                Opt(enc->priv_data, "crf", "23");
                Opt(enc->priv_data, "forced-idr", "1");
                break;
        }

        if (kind == EncoderKind.IntelQuickSync)
        {
            // Umgehung für die DLL-Fassung von FFmpeg: avutil und avcodec enthalten jeweils eine eigene Kopie
            // des Intel-oneVPL-Dispatchers. Mit dem "Loader" des QSV-Geräts aus avutil findet der Encoder (avcodec)
            // keine Implementierung ("Error creating a MFX session: -9", am 11.11.2026 auf dem N100 beobachtet).
            // Ohne Loader legt avcodec einen eigenen an und hängt seine Sitzung trotzdem an das gemeinsame Gerät.
            var framesCtx = (AVHWFramesContext*)ffmpeg.av_buffersink_get_hw_frames_ctx(sink)->data;
            var qsvDevice = (QsvDeviceContext*)framesCtx->device_ctx->hwctx;
            var savedLoader = qsvDevice->Loader;
            qsvDevice->Loader = IntPtr.Zero;
            try { FfmpegLib.Check(ffmpeg.avcodec_open2(enc, codec, null), $"{name} öffnen"); }
            finally { qsvDevice->Loader = savedLoader; }   // gehört weiter dem Gerät und wird mit ihm freigegeben
        }
        else
        {
            FfmpegLib.Check(ffmpeg.avcodec_open2(enc, codec, null), $"{name} öffnen");
        }

        // --- SPS/PPS vor jedes Schlüsselbild (dump_extra), damit der Client jederzeit einsteigen kann ---
        AVBSFContext* bsf;
        FfmpegLib.Check(ffmpeg.av_bsf_alloc(ffmpeg.av_bsf_get_by_name("dump_extra"), &bsf), "dump_extra");
        _bsf = bsf;
        FfmpegLib.Check(ffmpeg.avcodec_parameters_from_context(bsf->par_in, enc), "dump_extra Parameter");
        bsf->time_base_in = enc->time_base;
        FfmpegLib.Check(ffmpeg.av_bsf_init(bsf), "dump_extra init");

        Kind = kind;
        Log?.Invoke($"Video: Monitor {_monitor.Display} (Grafikkarte {_monitor.Adapter}, Ausgang {_monitor.Output}), " +
                    $"{width} × {height}, {_fps} fps, {Describe(kind)}");
        Started?.Invoke(width, height);
    }

    // ---- Encoder-Thread -------------------------------------------------------------------------

    private void EncodeLoop()
    {
        var packet = ffmpeg.av_packet_alloc();
        try
        {
            while (!_stop)
            {
                _slotFilled.WaitOne(500);
                AVFrame* frame;
                lock (_slotGate)
                {
                    frame = _slot;
                    _slot = null;
                }
                if (frame == null) continue;

                if (Interlocked.Exchange(ref _keyframeRequested, 0) == 1)
                {
                    frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
                    frame->flags |= ffmpeg.AV_FRAME_FLAG_KEY;
                }
                else
                {
                    frame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
                }

                long t0 = Stopwatch.GetTimestamp();
                int ret = ffmpeg.avcodec_send_frame(_enc, frame);
                ffmpeg.av_frame_free(&frame);
                FfmpegLib.Check(ret, "Kodieren");

                while (ffmpeg.avcodec_receive_packet(_enc, packet) >= 0)
                {
                    FfmpegLib.Check(ffmpeg.av_bsf_send_packet(_bsf, packet), "dump_extra");
                    while (ffmpeg.av_bsf_receive_packet(_bsf, packet) >= 0)
                    {
                        bool key = (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                        var data = new byte[packet->size];
                        new ReadOnlySpan<byte>(packet->data, packet->size).CopyTo(data);
                        ffmpeg.av_packet_unref(packet);

                        Interlocked.Add(ref _bytes, data.Length);
                        Interlocked.Increment(ref _encoded);
                        if (key) Interlocked.Increment(ref _keys);
                        _sendQueue.Add((data, key));
                    }
                }
                Interlocked.Add(ref _encodeTicks, Stopwatch.GetTimestamp() - t0);
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Failed?.Invoke(ex.Message);
            _stop = true;
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
        }
    }

    // ---- Sende-Thread ---------------------------------------------------------------------------

    private void SendLoop()
    {
        try
        {
            foreach (var (data, key) in _sendQueue.GetConsumingEnumerable())
            {
                long t0 = Stopwatch.GetTimestamp();
                PacketReady?.Invoke(data, key);
                Interlocked.Add(ref _sendTicks, Stopwatch.GetTimestamp() - t0);
                Interlocked.Increment(ref _sent);
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Failed?.Invoke(ex.Message);
        }
    }

    private void Close()
    {
        if (_bsf != null) { var b = _bsf; ffmpeg.av_bsf_free(&b); _bsf = null; }
        if (_enc != null) { var e = _enc; ffmpeg.avcodec_free_context(&e); _enc = null; }
        if (_graph != null) { var g = _graph; ffmpeg.avfilter_graph_free(&g); _graph = null; }
        if (_device != null) { var d = _device; ffmpeg.av_buffer_unref(&d); _device = null; }
    }

    /// <summary>AVQSVDeviceContext aus libavutil/hwcontext_qsv.h (FFmpeg 9.0): mfxSession session; void* loader.</summary>
    private struct QsvDeviceContext
    {
        public IntPtr Session;
        public IntPtr Loader;
    }

    public void Dispose()
    {
        _stop = true;
        _slotFilled.Set();
        if (_captureThread.IsAlive && Thread.CurrentThread != _captureThread)
            _captureThread.Join(TimeSpan.FromSeconds(4));
    }
}
