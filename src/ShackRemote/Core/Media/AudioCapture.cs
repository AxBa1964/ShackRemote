using FFmpeg.AutoGen;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ShackRemote.Core.Media;

/// <summary>
/// Server: nimmt Ton auf und kodiert ihn als Opus (48 kHz, Stereo, 10-ms-Pakete, "lowdelay").
/// Quelle wahlweise
///  - ein Wiedergabegerät per Loopback (was der Shack-PC über Lautsprecher abspielt) oder
///  - ein Aufnahmegerät (z. B. "USB Audio CODEC" des Funkgeräts – geteilter Modus, WSJT-X kann parallel lesen).
/// </summary>
public sealed unsafe class AudioCapture : IDisposable
{
    /// <summary>Geräte-Id für "was das Standard-Wiedergabegerät abspielt" (Loopback).</summary>
    public const string DefaultPlaybackId = "";
    private const int Rate = 48000;

    private readonly string _deviceId;
    private WasapiCapture? _capture;
    private AVCodecContext* _enc;
    private AVPacket* _packet;
    private readonly List<float> _pending = new();   // 48 kHz, Stereo, verschachtelt
    private readonly object _gate = new();
    private double _resamplePos;                      // Lineares Umrechnen auf 48 kHz
    private float _lastL, _lastR;
    private bool _stopped;

    /// <summary>Fertiges Opus-Paket (10 ms Ton); läuft im Aufnahme-Thread.</summary>
    public event Action<byte[]>? PacketReady;
    /// <summary>Meldungen für das Protokoll.</summary>
    public event Action<string>? Log;
    /// <summary>Aufnahme ist ausgefallen (z. B. Gerät entfernt).</summary>
    public event Action<string>? Failed;

    public AudioCapture(string deviceId) => _deviceId = deviceId;

    /// <summary>Alle aktiven Wiedergabe- und Aufnahmegeräte.</summary>
    public static List<AudioDevice> ListDevices()
    {
        var list = new List<AudioDevice>();
        using var e = new MMDeviceEnumerator();
        string? defaultRender = null;
        try { defaultRender = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName; } catch { }
        list.Add(new AudioDevice { Id = DefaultPlaybackId, Name = "Standard-Wiedergabe" + (defaultRender is null ? "" : $" ({defaultRender})"), Kind = "Wiedergabe" });
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            list.Add(new AudioDevice { Id = d.ID, Name = d.FriendlyName, Kind = "Wiedergabe" });
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            list.Add(new AudioDevice { Id = d.ID, Name = d.FriendlyName, Kind = "Aufnahme" });
        return list;
    }

    /// <summary>Gerät öffnen und Aufnahme starten.</summary>
    public void Start()
    {
        FfmpegLib.EnsureLoaded();
        OpenEncoder();

        using var e = new MMDeviceEnumerator();
        MMDevice device = _deviceId == DefaultPlaybackId
            ? e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : e.GetDevice(_deviceId);
        bool loopback = device.DataFlow == DataFlow.Render;
        _capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device, true, 20);
        var f = _capture.WaveFormat;
        Log?.Invoke($"Ton: {(loopback ? "Wiedergabe" : "Aufnahme")} \"{device.FriendlyName}\", {f.SampleRate} Hz, {f.Channels} Kanäle, {f.BitsPerSample} Bit {f.Encoding} → Opus 48 kHz");

        _capture.DataAvailable += (_, a) =>
        {
            try { OnData(a.Buffer, a.BytesRecorded, f); }
            catch (Exception ex) { Failed?.Invoke(ex.Message); }
        };
        _capture.RecordingStopped += (_, a) =>
        {
            if (!_stopped && a.Exception is not null) Failed?.Invoke($"Aufnahme beendet: {a.Exception.Message}");
        };
        _capture.StartRecording();
    }

    private void OpenEncoder()
    {
        var codec = ffmpeg.avcodec_find_encoder_by_name("libopus");
        if (codec == null) throw new InvalidOperationException("libopus fehlt in dieser FFmpeg-Version.");
        var enc = ffmpeg.avcodec_alloc_context3(codec);
        _enc = enc;
        enc->sample_rate = Rate;
        enc->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT;
        ffmpeg.av_channel_layout_default(&enc->ch_layout, 2);
        enc->bit_rate = 96000;
        ffmpeg.av_opt_set(enc->priv_data, "application", "lowdelay", 0);
        ffmpeg.av_opt_set(enc->priv_data, "frame_duration", "10", 0);
        FfmpegLib.Check(ffmpeg.avcodec_open2(enc, codec, null), "Opus-Encoder öffnen");
        _packet = ffmpeg.av_packet_alloc();
    }

    /// <summary>Beliebiges WASAPI-Format -> Stereo float -> 48 kHz -> Opus-Pakete.</summary>
    private void OnData(byte[] buffer, int bytes, WaveFormat f)
    {
        int channels = Math.Max(1, f.Channels);
        int bytesPerSample = f.BitsPerSample / 8;
        int frames = bytes / (bytesPerSample * channels);
        // Gemeinsames Mischformat von WASAPI ist praktisch immer 32-Bit-Gleitkomma
        bool isFloat = f.BitsPerSample == 32 && (f.Encoding == WaveFormatEncoding.IeeeFloat || f.Encoding == WaveFormatEncoding.Extensible);

        float Sample(int frame, int ch)
        {
            int o = (frame * channels + ch) * bytesPerSample;
            return bytesPerSample switch
            {
                4 when isFloat => BitConverter.ToSingle(buffer, o),
                4 => BitConverter.ToInt32(buffer, o) / 2147483648f,
                3 => ((buffer[o] << 8 | buffer[o + 1] << 16 | buffer[o + 2] << 24) >> 8) / 8388608f,
                2 => BitConverter.ToInt16(buffer, o) / 32768f,
                _ => 0f,
            };
        }

        lock (_gate)
        {
            if (_stopped || _enc == null) return;
            double step = (double)f.SampleRate / Rate;
            for (int i = 0; i < frames; i++)
            {
                float l = Sample(i, 0);
                float r = channels > 1 ? Sample(i, 1) : l;    // Mono verdoppeln, mehr als 2 Kanäle: nur vorne links/rechts
                if (f.SampleRate == Rate)
                {
                    _pending.Add(l);
                    _pending.Add(r);
                }
                else
                {
                    // Lineare Interpolation zwischen vorigem und aktuellem Eingangswert
                    while (_resamplePos <= 1.0)
                    {
                        float t = (float)_resamplePos;
                        _pending.Add(_lastL + (l - _lastL) * t);
                        _pending.Add(_lastR + (r - _lastR) * t);
                        _resamplePos += step;
                    }
                    _resamplePos -= 1.0;
                }
                _lastL = l;
                _lastR = r;
            }
            EncodeAvailable();
        }
    }

    private void EncodeAvailable()
    {
        int frameSize = _enc->frame_size > 0 ? _enc->frame_size : 480;
        while (_pending.Count >= frameSize * 2)
        {
            var frame = ffmpeg.av_frame_alloc();
            try
            {
                frame->nb_samples = frameSize;
                frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLT;
                frame->sample_rate = Rate;
                ffmpeg.av_channel_layout_copy(&frame->ch_layout, &_enc->ch_layout);
                FfmpegLib.Check(ffmpeg.av_frame_get_buffer(frame, 0), "Tonpuffer");
                var dst = new Span<float>(frame->data[0], frameSize * 2);
                for (int i = 0; i < frameSize * 2; i++) dst[i] = _pending[i];
                _pending.RemoveRange(0, frameSize * 2);
                FfmpegLib.Check(ffmpeg.avcodec_send_frame(_enc, frame), "Ton kodieren");
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
            }

            while (ffmpeg.avcodec_receive_packet(_enc, _packet) >= 0)
            {
                var data = new byte[_packet->size];
                new ReadOnlySpan<byte>(_packet->data, _packet->size).CopyTo(data);
                ffmpeg.av_packet_unref(_packet);
                PacketReady?.Invoke(data);
            }
        }
    }

    /// <summary>Aufnahme beenden und Encoder freigeben.</summary>
    public void Dispose()
    {
        lock (_gate) _stopped = true;
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _capture = null;
        lock (_gate)
        {
            if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
            if (_enc != null) { var e = _enc; ffmpeg.avcodec_free_context(&e); _enc = null; }
        }
    }
}
