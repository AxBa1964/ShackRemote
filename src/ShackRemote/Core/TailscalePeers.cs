using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ShackRemote.Core;

/// <summary>
/// Geräte im eigenen Tailscale-Netz über "tailscale status --json" (Feld Peer → HostName, Online, TailscaleIPs).
/// Über Tailscale gibt es keinen Broadcast – deshalb fragt die Server-Suche diese Geräte gezielt an.
/// Ohne installiertes Tailscale oder bei einem Fehler: leere Liste.
/// </summary>
public static class TailscalePeers
{
    /// <summary>Ein Gerät im Tailscale-Netz mit Namen und IPv4-Adresse.</summary>
    public sealed record Peer(string Name, IPAddress Address);

    /// <summary>Fragt "tailscale status --json" ab und liefert alle Geräte, die gerade online sind (höchstens 4 s).</summary>
    public static async Task<List<Peer>> OnlineAsync(Action<string>? log = null)
    {
        var result = new List<Peer>();
        var exe = FindExe();
        if (exe is null) return result;

        try
        {
            var psi = new ProcessStartInfo(exe, "status --json")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return result;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            if (p.ExitCode != 0) return result;

            using var doc = JsonDocument.Parse(output);
            if (!doc.RootElement.TryGetProperty("Peer", out var peers) || peers.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var peer in peers.EnumerateObject())
            {
                var v = peer.Value;
                bool online = v.TryGetProperty("Online", out var o) && o.ValueKind == JsonValueKind.True;
                if (!online) continue;
                var name = v.TryGetProperty("HostName", out var h) ? h.GetString() ?? "?" : "?";
                if (!v.TryGetProperty("TailscaleIPs", out var ips) || ips.ValueKind != JsonValueKind.Array) continue;
                foreach (var ip in ips.EnumerateArray())
                    if (IPAddress.TryParse(ip.GetString(), out var a) && a.AddressFamily == AddressFamily.InterNetwork)
                        result.Add(new Peer(name, a));
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Tailscale-Geräte nicht abfragbar: {ex.Message}");
        }
        return result;
    }

    private static string? FindExe()
    {
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (File.Exists(standard)) return standard;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "tailscale.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ungültiger Eintrag */ }
        }
        return null;
    }
}
