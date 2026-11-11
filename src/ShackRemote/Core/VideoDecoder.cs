using System.Diagnostics;

namespace ShackRemote.Core;

/// <summary>
/// Client: dekodiert den H.264-Datenstrom mit ffmpeg und liefert fertige BGRA-Bilder in der
/// gewünschten Anzeigegröße (Skalierung übernimmt ffmpeg, bikubisch).
/// </summary>
public sealed class VideoDecoder : IDisposable
{
    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Process _process;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Neues Bild: Puffer (wird wiederverwendet – sofort kopieren!), Breite, Höhe.</summary>
    public event Action<byte[], int, int>? FrameReady;
    public event Action<string>? Log;

    public VideoDecoder(int width, int height)
    {
        Width = width;
        Height = height;

        _process = Ffmpeg.Start(new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-threads", "1",                       // keine Frame-Threads: weniger Verzögerung
            "-fflags", "nobuffer", "-flags", "low_delay",
            "-probesize", "32", "-analyzeduration", "0",
            "-f", "h264", "-i", "-",
            "-vf", $"scale={width}:{height}:flags=bicubic",
            "-pix_fmt", "bgra",
            "-fps_mode", "passthrough",
            "-flush_packets", "1",
            "-f", "rawvideo", "-",
        }, redirectInput: true);

        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log?.Invoke($"Decoder: {e.Data}"); };
        _process.BeginErrorReadLine();

        var thread = new Thread(ReadFrames) { IsBackground = true, Name = "ShackRemote Decoder" };
        thread.Start();
    }

    /// <summary>H.264-Daten vom Server weiterreichen (aus der Leseschleife der Sitzung).</summary>
    public void Write(byte[] data)
    {
        lock (_writeGate)
        {
            if (_cts.IsCancellationRequested) return;
            try
            {
                var stdin = _process.StandardInput.BaseStream;
                stdin.Write(data, 0, data.Length);
                stdin.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Decoder wurde beendet (z. B. Neustart bei Größenänderung)
            }
        }
    }

    private void ReadFrames()
    {
        var frame = new byte[Width * Height * 4];
        try
        {
            var stdout = _process.StandardOutput.BaseStream;
            while (!_cts.IsCancellationRequested)
            {
                stdout.ReadExactly(frame);
                FrameReady?.Invoke(frame, Width, Height);
            }
        }
        catch (Exception)
        {
            // Ende des Prozesses
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_writeGate)
        {
            Ffmpeg.Kill(_process);
        }
    }
}
