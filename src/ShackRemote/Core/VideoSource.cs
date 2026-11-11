using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ShackRemote.Core;

/// <summary>
/// Server: nimmt einen Monitor per Desktop Duplication auf, kodiert per Intel Quick Sync (H.264)
/// und schickt den Datenstrom als Video-Nachrichten über die bestehende TLS-Verbindung.
/// Befehl wie in Stufe 0 getestet, plus Low-Latency-Optionen.
/// </summary>
public sealed partial class VideoSource : IDisposable
{
    private readonly FramedConnection _conn;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _cts = new();
    private Process? _process;
    private int _infoSent;

    public VideoSource(FramedConnection conn, Action<string> log)
    {
        _conn = conn;
        _log = log;
    }

    public async Task StartAsync(int monitor, int fps)
    {
        await _conn.SendControlAsync(new ControlMessage { Type = "VideoReset", Monitor = monitor }, _cts.Token);

        _process = Ffmpeg.Start(new[]
        {
            "-hide_banner", "-loglevel", "info", "-nostats",
            "-init_hw_device", "d3d11va:,vendor_id=0x8086",
            "-filter_complex", $"ddagrab=output_idx={monitor}:framerate={fps},hwmap=derive_device=qsv,format=qsv",
            "-c:v", "h264_qsv", "-global_quality", "20",
            "-async_depth", "1", "-bf", "0", "-g", fps.ToString(),
            "-bsf:v", "dump_extra",          // SPS/PPS vor jedem Schlüsselbild: Client kann jederzeit einsteigen
            "-flush_packets", "1",
            "-f", "h264", "-",
        }, redirectInput: false);

        _process.ErrorDataReceived += (_, e) => OnStderr(e.Data);
        _process.BeginErrorReadLine();
        _log($"Video: Monitor {monitor + 1} (DXGI-Ausgang {monitor}), {fps} fps gestartet");
        _ = Task.Run(PumpAsync);
    }

    private void OnStderr(string? line)
    {
        if (line is null) return;

        // Beispiel: "Stream #0:0: Video: h264, bgra(...), 1920x1080 [SAR 1:1 DAR 16:9], ..."
        if (Volatile.Read(ref _infoSent) == 0 && line.Contains("Video:") && line.Contains("Stream #"))
        {
            var m = SizeRegex().Match(line);
            if (m.Success && Interlocked.Exchange(ref _infoSent, 1) == 0)
            {
                int w = int.Parse(m.Groups[1].Value), h = int.Parse(m.Groups[2].Value);
                _log($"Video: Auflösung {w} × {h}");
                _ = _conn.SendControlAsync(new ControlMessage { Type = "VideoInfo", Width = w, Height = h }, _cts.Token);
            }
        }

        if (line.Contains("rror", StringComparison.Ordinal) || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            _log($"ffmpeg: {line}");
    }

    private async Task PumpAsync()
    {
        var ct = _cts.Token;
        var buffer = new byte[256 * 1024];
        long bytes = 0, chunks = 0;
        var report = Stopwatch.StartNew();
        try
        {
            var stdout = _process!.StandardOutput.BaseStream;
            while (!ct.IsCancellationRequested)
            {
                int n = await stdout.ReadAsync(buffer, ct);
                if (n == 0) break;
                await _conn.SendAsync(MsgType.Video, buffer.AsMemory(0, n), ct);
                bytes += n;
                chunks++;
                if (report.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    _log($"Video: {bytes / 1024} KB in {chunks} Paketen gesendet ({bytes * 8 / 1000 / report.Elapsed.TotalSeconds:F0} kbit/s)");
                    bytes = 0; chunks = 0; report.Restart();
                }
            }
            if (!ct.IsCancellationRequested)
                _log("Video: ffmpeg wurde beendet (siehe Meldungen oben).");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log($"Video beendet: {ex.Message}");
        }
        catch (Exception)
        {
            // gewolltes Ende
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Ffmpeg.Kill(_process);
        _process = null;
    }

    [GeneratedRegex(@"\b(\d{3,5})x(\d{3,5})\b")]
    private static partial Regex SizeRegex();
}
