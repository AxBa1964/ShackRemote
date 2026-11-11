using System.Collections.Concurrent;
using System.Diagnostics;

namespace ShackRemote.Core;

/// <summary>
/// Client: dekodiert den H.264-Datenstrom mit ffmpeg und liefert fertige BGRA-Bilder in der
/// gewünschten Anzeigegröße (Skalierung übernimmt ffmpeg, bikubisch).
///
/// Gegen wachsende Verzögerung: Daten landen erst in einer Warteschlange. Sind die ältesten Daten
/// älter als <see cref="MaxQueueAge"/>, wird alles verworfen bis zum nächsten Schlüsselbild
/// (SPS-Kennung im Datenstrom). Lieber kurz ein Standbild als dauerhaft Sekunden hinterher.
/// </summary>
public sealed class VideoDecoder : IDisposable
{
    private static readonly TimeSpan MaxQueueAge = TimeSpan.FromMilliseconds(150);

    private readonly CancellationTokenSource _cts = new();
    private readonly BlockingCollection<(byte[] Data, long Time)> _queue = new(new ConcurrentQueue<(byte[], long)>());
    private readonly Process _process;
    private int _drops;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Anzahl der Resynchronisierungen (Verwerfen bis zum nächsten Schlüsselbild) seit dem letzten Abruf.</summary>
    public int TakeDrops() => Interlocked.Exchange(ref _drops, 0);

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

        new Thread(WriteLoop) { IsBackground = true, Name = "ShackRemote Decoder-Eingang" }.Start();
        new Thread(ReadFrames) { IsBackground = true, Name = "ShackRemote Decoder-Ausgang" }.Start();
    }

    /// <summary>H.264-Daten vom Server übergeben. Blockiert nie (die Netzwerk-Leseschleife darf nicht hängen).</summary>
    public void Write(byte[] data)
    {
        if (_cts.IsCancellationRequested) return;
        try { _queue.Add((data, Stopwatch.GetTimestamp())); }
        catch (InvalidOperationException) { /* bereits beendet */ }
    }

    private void WriteLoop()
    {
        bool dropping = false;
        try
        {
            var stdin = _process.StandardInput.BaseStream;
            foreach (var (data, time) in _queue.GetConsumingEnumerable(_cts.Token))
            {
                if (!dropping && Stopwatch.GetElapsedTime(time) > MaxQueueAge)
                {
                    dropping = true;
                    Interlocked.Increment(ref _drops);
                }

                int offset = 0;
                if (dropping)
                {
                    offset = FindSps(data);
                    if (offset < 0) continue;        // weiter verwerfen
                    dropping = false;                // ab hier sauberer Neueinstieg
                }

                stdin.Write(data, offset, data.Length - offset);
                stdin.Flush();
            }
        }
        catch (Exception)
        {
            // Ende: Abbruch oder Prozess beendet. Dispose() beendet den Prozess, dadurch löst sich ein hängendes Write.
        }
    }

    /// <summary>Sucht den Beginn einer SPS-Einheit (NAL-Typ 7, Startcode 00 00 01). -1 = nicht gefunden.</summary>
    private static int FindSps(byte[] d)
    {
        for (int i = 0; i + 3 < d.Length; i++)
        {
            if (d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1 && (d[i + 3] & 0x1F) == 7)
                return i > 0 && d[i - 1] == 0 ? i - 1 : i;   // vierbytigen Startcode mitnehmen
        }
        return -1;
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
        // Ohne Sperre: Das Beenden des Prozesses löst blockierende Lese-/Schreibvorgänge.
        // (Version 0.2.0 hielt hier eine Sperre, die ein hängendes Write festhielt -> eingefrorene Oberfläche.)
        _cts.Cancel();
        _queue.CompleteAdding();
        Ffmpeg.Kill(_process);
    }
}
