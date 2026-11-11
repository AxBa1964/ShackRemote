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
                using var video = new VideoChannel(conn, VideoFps, msg => Log?.Invoke(msg));
                MonitorInfo? shown = null;   // Monitor, den der Client gerade sieht (für Mauskoordinaten)
                ClientConnected?.Invoke($"{name} ({remote})");

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var (type, payload) = await conn.ReadAsync(ct);
                        if (type == MsgType.Input)
                        {
                            try { InputInjector.Apply(payload, shown); }
                            catch (Exception ex) { Log?.Invoke($"Eingabe fehlgeschlagen: {ex.Message}"); }
                            continue;
                        }
                        if (type != MsgType.Control) continue;
                        var m = System.Text.Json.JsonSerializer.Deserialize<ControlMessage>(payload);
                        switch (m?.Type)
                        {
                            case "Ping":
                                await conn.SendControlAsync(new ControlMessage { Type = "Pong", T = m.T }, ct);
                                break;
                            case "StartVideo":
                                video.Start(Math.Clamp(m.Monitor, 0, 15));
                                shown = monitors?.FirstOrDefault(mo => mo.Index == m.Monitor);
                                break;
                            case "RequestKeyframe":
                                video.RequestKeyframe();
                                break;
                            case "StopVideo":
                                video.Stop();
                                Log?.Invoke("Video gestoppt");
                                break;
                        }
                    }
                }
                finally
                {
                    video.Stop();
                    InputInjector.ReleaseAll();
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

    /// <summary>
    /// Bildkanal einer Sitzung. Verliert Windows die Bildschirmaufnahme (z. B. Sperrbildschirm, Monitor-Standby,
    /// Auflösungswechsel, UAC: DXGI_ERROR_ACCESS_LOST), wird sie alle 2 s neu aufgebaut, bis es wieder geht.
    /// </summary>
    private sealed class VideoChannel : IDisposable
    {
        private readonly FramedConnection _conn;
        private readonly int _fps;
        private readonly Action<string> _log;
        private readonly object _gate = new();
        private ScreenEncoder? _encoder;
        private int _output;
        private int _failures;
        private string? _lastError;

        public VideoChannel(FramedConnection conn, int fps, Action<string> log)
        {
            _conn = conn;
            _fps = fps;
            _log = log;
        }

        public void Start(int output)
        {
            lock (_gate)
            {
                StopLocked();
                _output = output;
                Interlocked.Exchange(ref _failures, 0);
                _lastError = null;
                StartLocked();
            }
        }

        public void RequestKeyframe()
        {
            lock (_gate) _encoder?.RequestKeyframe();
        }

        public void Stop()
        {
            lock (_gate) StopLocked();
        }

        public void Dispose() => Stop();

        private void StopLocked()
        {
            _encoder?.Dispose();
            _encoder = null;
        }

        private void StartLocked()
        {
            int output = _output;
            _conn.SendControlAsync(new ControlMessage { Type = "VideoReset", Monitor = output }).GetAwaiter().GetResult();
            var encoder = new ScreenEncoder(output, _fps);
            encoder.Log += _log;
            encoder.Started += (w, h) =>
            {
                // Läuft im Aufnahme-Thread: hier KEIN lock(_gate) – Stop() hält ihn und wartet auf diesen Thread.
                _ = _conn.SendControlAsync(new ControlMessage { Type = "VideoInfo", Monitor = output, Width = w, Height = h });
            };
            encoder.Failed += msg => OnFailed(encoder, msg);
            encoder.PacketReady += (data, key) =>
            {
                // Läuft im Sende-Thread des Encoders; Warten aufs Netz bremst Aufnahme und Kodierung nicht.
                // Erst wenn wieder Bilder fließen, gilt die Unterbrechung als beendet.
                if (Volatile.Read(ref _failures) > 0)
                {
                    int failures = Interlocked.Exchange(ref _failures, 0);
                    if (failures > 0) _log($"Video: Aufnahme läuft wieder (nach {failures} Versuch(en))");
                    _lastError = null;
                }
                try { _conn.SendAsync(MsgType.Video, VideoPacket.Build(data, key)).GetAwaiter().GetResult(); }
                catch { /* Verbindung weg – Ende meldet die Leseschleife */ }
            };
            _encoder = encoder;
            try
            {
                encoder.Start();
            }
            catch (Exception ex)
            {
                OnFailed(encoder, ex.Message);
            }
        }

        private void OnFailed(ScreenEncoder encoder, string message)
        {
            // Läuft meist in einem Encoder-Thread: hier KEIN lock(_gate) (siehe Started).
            if (!ReferenceEquals(Volatile.Read(ref _encoder), encoder)) return;   // alter Encoder, schon ersetzt
            int failures = Interlocked.Increment(ref _failures);
            // Nicht bei jedem Versuch protokollieren: erste Meldung, neue Meldung, dann alle 30 Versuche (~1 min)
            if (message != _lastError || failures % 30 == 1)
            {
                _log($"Video unterbrochen: {message} – neuer Versuch alle 2 s (Sperrbildschirm/Monitor-Standby?)");
                _ = _conn.SendControlAsync(new ControlMessage { Type = "VideoError", Text = $"{message} – wird neu versucht" });
            }
            _lastError = message;

            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                lock (_gate)
                {
                    if (!ReferenceEquals(_encoder, encoder)) return;   // inzwischen gestoppt oder anderer Monitor
                    encoder.Dispose();
                    _encoder = null;
                    try { StartLocked(); }
                    catch (Exception ex) { _log($"Video-Neustart fehlgeschlagen: {ex.Message}"); }
                }
            });
        }
    }

    public void Dispose() => Stop();
}
