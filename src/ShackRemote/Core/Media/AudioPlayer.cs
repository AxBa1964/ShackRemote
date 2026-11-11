using FFmpeg.AutoGen;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ShackRemote.Core.Media;

/// <summary>
/// Client: Opus-Pakete dekodieren und über das Standard-Wiedergabegerät abspielen (WASAPI, geteilter Modus).
/// Hält die Verzögerung klein: Liegt mehr als 250 ms im Puffer (z. B. nach einer Netzstockung), wird er geleert.
/// </summary>
public sealed unsafe class AudioPlayer : IDisposable
{
    private const int Rate = 48000;
    private static readonly TimeSpan MaxBuffered = TimeSpan.FromMilliseconds(250);

    private AVCodecContext* _dec;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private WasapiOut? _out;
    private BufferedWaveProvider? _buffer;
    private readonly object _gate = new();
    private byte[] _pcm = new byte[4096];

    public event Action<string>? Log;
    public int Drops;

    public void Start()
    {
        FfmpegLib.EnsureLoaded();

        // libopus liefert verschachteltes float (wie die Wiedergabe es braucht); sonst eingebauter Opus-Decoder
        var codec = ffmpeg.avcodec_find_decoder_by_name("libopus");
        if (codec == null) codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_OPUS);
        var dec = ffmpeg.avcodec_alloc_context3(codec);
        _dec = dec;
        dec->sample_rate = Rate;
        ffmpeg.av_channel_layout_default(&dec->ch_layout, 2);
        dec->request_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        FfmpegLib.Check(ffmpeg.avcodec_open2(dec, codec, null), "Opus-Decoder öffnen");
        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();

        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
            ReadFully = true,   // bei Lücken Stille statt Abbruch
        };
        _out = new WasapiOut(AudioClientShareMode.Shared, true, 40);
        _out.Init(_buffer);
        _out.Play();
        Log?.Invoke("Ton: Wiedergabe gestartet (Opus 48 kHz Stereo)");
    }

    /// <summary>Aus der Netzwerk-Leseschleife: ein Opus-Paket.</summary>
    public void Submit(byte[] data)
    {
        lock (_gate)
        {
            if (_dec == null || _buffer == null) return;
            fixed (byte* p = data)
            {
                _packet->data = p;
                _packet->size = data.Length;
                int ret = ffmpeg.avcodec_send_packet(_dec, _packet);
                _packet->data = null;
                _packet->size = 0;
                if (ret < 0) return;
            }

            while (ffmpeg.avcodec_receive_frame(_dec, _frame) >= 0)
            {
                int n = _frame->nb_samples;
                int channels = _frame->ch_layout.nb_channels;
                int bytes = n * 2 * sizeof(float);
                if (_pcm.Length < bytes) _pcm = new byte[bytes];
                var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(_pcm.AsSpan(0, bytes));

                var fmt = (AVSampleFormat)_frame->format;
                for (int i = 0; i < n; i++)
                {
                    float l, r;
                    if (fmt == AVSampleFormat.AV_SAMPLE_FMT_FLT)
                    {
                        var s = (float*)_frame->data[0];
                        l = s[i * channels];
                        r = channels > 1 ? s[i * channels + 1] : l;
                    }
                    else if (fmt == AVSampleFormat.AV_SAMPLE_FMT_FLTP)
                    {
                        l = ((float*)_frame->data[0])[i];
                        r = channels > 1 ? ((float*)_frame->data[1])[i] : l;
                    }
                    else if (fmt == AVSampleFormat.AV_SAMPLE_FMT_S16)
                    {
                        var s = (short*)_frame->data[0];
                        l = s[i * channels] / 32768f;
                        r = channels > 1 ? s[i * channels + 1] / 32768f : l;
                    }
                    else
                    {
                        l = r = 0;
                    }
                    dst[i * 2] = l;
                    dst[i * 2 + 1] = r;
                }
                ffmpeg.av_frame_unref(_frame);

                if (_buffer.BufferedDuration > MaxBuffered)
                {
                    _buffer.ClearBuffer();   // Rückstand: lieber kurz springen als dauerhaft verzögert
                    Interlocked.Increment(ref Drops);
                }
                _buffer.AddSamples(_pcm, 0, bytes);
            }
        }
    }

    /// <summary>Wie viel Ton gerade im Puffer liegt (Verzögerung beim Laptop).</summary>
    public TimeSpan Buffered => _buffer?.BufferedDuration ?? TimeSpan.Zero;

    public void Dispose()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _out = null;
        lock (_gate)
        {
            _buffer = null;
            if (_frame != null) { var f = _frame; ffmpeg.av_frame_free(&f); _frame = null; }
            if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
            if (_dec != null) { var d = _dec; ffmpeg.avcodec_free_context(&d); _dec = null; }
        }
    }
}
