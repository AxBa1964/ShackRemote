using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using ShackRemote.Core.Media;

namespace ShackRemote.Core;

/// <summary>
/// Server: nimmt TLS-Verbindungen auf TCP 47101 an – nur auf den ausdrücklich gewählten IPv4-Adressen.
/// Immer nur ein Client gleichzeitig.
/// </summary>
public sealed class SessionServer : IDisposable
{
    private readonly X509Certificate2 _cert;
    private readonly string _fingerprint;
    private readonly List<TcpListener> _listeners = new();
    private CancellationTokenSource _cts = new();
    private int _busy;

    /// <summary>Bildrate der Aufnahme. 60 fps wurden in Stufe 0 auf dem N100 erreicht (54–57 fps).</summary>
    public int VideoFps { get; set; } = 60;

    public event Action<string>? Log;
    public event Action<string, string>? PairingCodeShown;   // Code, Client-Name
    public event Action? PairingEnded;
    public event Action<string>? ClientConnected;
    public event Action? ClientDisconnected;

    public SessionServer(X509Certificate2 cert)
    {
        _cert = cert;
        _fingerprint = Identity.Fingerprint(cert);
    }

    public void Start(IEnumerable<IPAddress> addresses)
    {
        Stop();
        foreach (var address in addresses)
        {
            try
            {
                var listener = new TcpListener(address, Protocol.SessionPort);
                listener.Start();
                _listeners.Add(listener);
                _ = AcceptLoopAsync(listener, _cts.Token);
                Log?.Invoke($"Sitzung: lausche auf {address}:{Protocol.SessionPort}");
            }
            catch (SocketException ex)
            {
                Log?.Invoke($"Kann nicht auf {address}:{Protocol.SessionPort} lauschen: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        foreach (var l in _listeners) l.Stop();
        _listeners.Clear();
        _cts = new CancellationTokenSource();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception) { return; } // Stop() oder Adapter weg
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient tcp, CancellationToken ct)
    {
        var remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            Log?.Invoke($"Verbindung von {remote} abgewiesen: bereits ein Client verbunden");
            tcp.Dispose();
            return;
        }

        bool pairingShown = false;
        bool connected = false;
        try
        {
            using (tcp)
            using (var ssl = new SslStream(tcp.GetStream(), false))
            {
                tcp.NoDelay = true;
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _cert,
                    ClientCertificateRequired = true,
                    // Selbst signierte Zertifikate: Vertrauen entsteht über die Kopplung, nicht über eine CA.
                    RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null,
                }, ct);

                var clientFp = Identity.Fingerprint(ssl.RemoteCertificate!);
                var conn = new FramedConnection(ssl);

                var hello = await conn.ReadControlAsync(ct);
                if (hello.Type != "Hello") throw new InvalidDataException("Hello erwartet.");
                var name = string.IsNullOrWhiteSpace(hello.Name) ? remote : hello.Name;
                Log?.Invoke($"{name} ({remote}) meldet sich, Version {hello.Version}, Zertifikat {Identity.Short(clientFp)}");

                var trusted = AppSettings.Read(s => s.TrustedClients.Any(c => string.Equals(c.Fingerprint, clientFp, StringComparison.OrdinalIgnoreCase)));
                if (!trusted)
                {
                    var code = Pairing.NewCode();
                    pairingShown = true;
                    PairingCodeShown?.Invoke(code, name);
                    await conn.SendControlAsync(new ControlMessage { Type = "PairRequired" }, ct);

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    var proof = await conn.ReadControlAsync(timeout.Token);

                    var ok = proof.Type == "PairProof" && Pairing.Matches(Pairing.Proof(code, clientFp, _fingerprint), proof.Mac);
                    PairingEnded?.Invoke();
                    pairingShown = false;
                    await conn.SendControlAsync(new ControlMessage { Type = "PairResult", Ok = ok, Text = ok ? "Gekoppelt" : "Falscher Code" }, ct);
                    if (!ok)
                    {
                        Log?.Invoke($"Kopplung mit {name} fehlgeschlagen (falscher Code)");
                        return;
                    }

                    AppSettings.Update(s => s.TrustedClients.Add(new TrustedClient { Fingerprint = clientFp, Name = name, PairedAt = DateTime.Now }));
                    Log?.Invoke($"{name} gekoppelt");
                }

                List<MonitorInfo>? monitors = null;
                try
                {
                    monitors = DxgiOutputs.List();
                    Log?.Invoke("Monitore: " + string.Join(", ", monitors.Select(mo =>
                        $"Anzeige {mo.Display} = Ausgang {mo.Index} ({mo.Width}×{mo.Height} bei {mo.Left},{mo.Top})")));
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"Monitore können nicht ermittelt werden: {ex.Message}");
                }

                await conn.SendControlAsync(new ControlMessage
                {
                    Type = "Welcome", Name = Environment.MachineName, Version = Protocol.Version, Monitors = monitors,
                }, ct);
                connected = true;
                ScreenEncoder? video = null;
                ClientConnected?.Invoke($"{name} ({remote})");

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var (type, payload) = await conn.ReadAsync(ct);
                        if (type != MsgType.Control) continue; // Eingabe folgt in Stufe 1c
                        var m = System.Text.Json.JsonSerializer.Deserialize<ControlMessage>(payload);
                        switch (m?.Type)
                        {
                            case "Ping":
                                await conn.SendControlAsync(new ControlMessage { Type = "Pong", T = m.T }, ct);
                                break;
                            case "StartVideo":
                                video?.Dispose();
                                video = StartEncoder(conn, Math.Clamp(m.Monitor, 0, 15));
                                break;
                            case "RequestKeyframe":
                                video?.RequestKeyframe();
                                break;
                            case "StopVideo":
                                video?.Dispose();
                                video = null;
                                Log?.Invoke("Video gestoppt");
                                break;
                        }
                    }
                }
                finally
                {
                    video?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Verbindung {remote} beendet: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (pairingShown) PairingEnded?.Invoke();
            if (connected) ClientDisconnected?.Invoke();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private ScreenEncoder? StartEncoder(FramedConnection conn, int output)
    {
        conn.SendControlAsync(new ControlMessage { Type = "VideoReset", Monitor = output }).GetAwaiter().GetResult();
        var encoder = new ScreenEncoder(output, VideoFps);
        encoder.Log += msg => Log?.Invoke(msg);
        encoder.Started += (w, h) => _ = conn.SendControlAsync(new ControlMessage { Type = "VideoInfo", Monitor = output, Width = w, Height = h });
        encoder.Failed += msg =>
        {
            Log?.Invoke($"Video kann nicht laufen: {msg}");
            _ = conn.SendControlAsync(new ControlMessage { Type = "VideoError", Text = msg });
        };
        encoder.PacketReady += (data, key) =>
        {
            // Läuft im Encoder-Thread. Warten ist hier gewollt: Ist das Netz langsamer, nimmt ddagrab
            // einfach das jeweils neueste Bild – es entsteht kein Stau.
            try { conn.SendAsync(MsgType.Video, VideoPacket.Build(data, key)).GetAwaiter().GetResult(); }
            catch { /* Verbindung weg – Ende meldet die Leseschleife */ }
        };
        try
        {
            encoder.Start();
            return encoder;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Video kann nicht starten: {ex.Message}");
            _ = conn.SendControlAsync(new ControlMessage { Type = "VideoError", Text = ex.Message });
            encoder.Dispose();
            return null;
        }
    }

    public void Dispose() => Stop();
}
