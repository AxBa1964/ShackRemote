using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ShackRemote.Core;

/// <summary>Gefundener Server: seine Selbstauskunft und die Adresse, von der die Antwort kam.</summary>
public sealed record FoundServer(ServerInfo Info, IPAddress RespondedFrom);

/// <summary>Server: beantwortet Suchanfragen auf UDP 47100 (IPv4, alle Adapter).</summary>
public sealed class DiscoveryResponder : IDisposable
{
    private readonly Func<ServerInfo> _info;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;

    /// <summary>Meldungen für das Protokoll.</summary>
    public event Action<string>? Log;

    public DiscoveryResponder(Func<ServerInfo> info) => _info = info;

    /// <summary>Beginnt auf UDP 47100 (IPv4) auf Suchanfragen zu antworten.</summary>
    public void Start()
    {
        // Ausdrücklich IPv4 (0.0.0.0) – siehe Stufe 0: "@" bzw. Standard landet unter Windows auf IPv6.
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, Protocol.DiscoveryPort)) { EnableBroadcast = true };
        _ = Task.Run(LoopAsync);
        Log?.Invoke($"Server-Suche: lausche auf UDP {Protocol.DiscoveryPort}");
    }

    private async Task LoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested && _udp is not null)
        {
            try
            {
                var r = await _udp.ReceiveAsync(ct);
                if (Encoding.UTF8.GetString(r.Buffer) != Protocol.DiscoverRequest) continue;
                var reply = JsonSerializer.SerializeToUtf8Bytes(_info());
                await _udp.SendAsync(reply, reply.Length, r.RemoteEndPoint);
                Log?.Invoke($"Suchanfrage von {r.RemoteEndPoint.Address} beantwortet");
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { /* ICMP von früherem Senden */ }
            catch (Exception ex) { Log?.Invoke($"Server-Suche: {ex.Message}"); }
        }
    }

    /// <summary>Beendet das Antworten und gibt den UDP-Port frei.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
    }
}

/// <summary>Client: sucht Server per Broadcast in allen eigenen Netzen und fragt bekannte Adressen gezielt an.</summary>
public static class DiscoveryClient
{
    /// <summary>Sucht Server: Broadcast in alle lokalen Netze mit Broadcast-Adresse plus gezielte Anfragen an <paramref name="unicastTargets"/> (bekannte Adressen, Tailscale-Geräte). Sammelt Antworten bis <paramref name="timeout"/>.</summary>
    public static async Task<List<FoundServer>> SearchAsync(IEnumerable<IPAddress> unicastTargets, TimeSpan timeout, Action<string>? log = null)
    {
        var found = new Dictionary<string, FoundServer>(StringComparer.OrdinalIgnoreCase);
        var request = Encoding.UTF8.GetBytes(Protocol.DiscoverRequest);
        var sockets = new List<UdpClient>();

        try
        {
            foreach (var la in NetInfo.GetAll())
            {
                if (la.Broadcast is null) continue;
                try
                {
                    var u = new UdpClient(new IPEndPoint(la.Address, 0)) { EnableBroadcast = true };
                    sockets.Add(u);
                    await u.SendAsync(request, request.Length, new IPEndPoint(la.Broadcast, Protocol.DiscoveryPort));
                    log?.Invoke($"Suche per Broadcast über {la.Address} ({la.Kind}) an {la.Broadcast}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Broadcast über {la.Address} nicht möglich: {ex.Message}");
                }
            }

            var any = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            sockets.Add(any);
            foreach (var target in unicastTargets.Distinct())
            {
                try
                {
                    await any.SendAsync(request, request.Length, new IPEndPoint(target, Protocol.DiscoveryPort));
                    log?.Invoke($"Gezielte Anfrage an {target}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Anfrage an {target} nicht möglich: {ex.Message}");
                }
            }

            using var cts = new CancellationTokenSource(timeout);
            await Task.WhenAll(sockets.Select(s => ReceiveAsync(s, found, cts.Token)));
        }
        finally
        {
            foreach (var s in sockets) s.Dispose();
        }

        lock (found) return found.Values.ToList();
    }

    private static async Task ReceiveAsync(UdpClient socket, Dictionary<string, FoundServer> found, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                var r = await socket.ReceiveAsync(ct);
                var info = JsonSerializer.Deserialize<ServerInfo>(r.Buffer);
                if (info is null || string.IsNullOrEmpty(info.Fingerprint)) continue;
                lock (found)
                {
                    if (!found.ContainsKey(info.Fingerprint))
                        found[info.Fingerprint] = new FoundServer(info, r.RemoteEndPoint.Address);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { /* Ziel ohne Server */ }
            catch (JsonException) { /* fremdes Paket */ }
            catch (SocketException) { return; }
        }
    }
}
