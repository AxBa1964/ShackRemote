using System.Diagnostics;

namespace ShackRemote.Core;

/// <summary>
/// Stufe 1b nutzt ffmpeg.exe als Hilfsprozess (Aufnahme/Kodierung bzw. Dekodierung).
/// Gesucht wird neben ShackRemote.exe und dann im PATH (z. B. nach "winget install ffmpeg").
/// </summary>
public static class Ffmpeg
{
    public static string? Find()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim()), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* ungültiger PATH-Eintrag */ }
        }
        return null;
    }

    public static Process Start(IEnumerable<string> arguments, bool redirectInput)
    {
        var exe = Find() ?? throw new FileNotFoundException(
            "ffmpeg.exe wurde nicht gefunden. Bitte neben ShackRemote.exe legen oder installieren (winget install ffmpeg).");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg konnte nicht gestartet werden.");
    }

    public static void Kill(Process? p)
    {
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
        p.Dispose();
    }
}
