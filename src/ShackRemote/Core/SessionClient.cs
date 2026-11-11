using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace ShackRemote.Core;

/// <summary>Client: baut die TLS-Verbindung auf, koppelt bei Bedarf und misst die Umlaufzeit (Ping/Pong).</summary>
public sealed class SessionClient : IAsyncDisposable
{
    private readonly X509Certificate2 _cert;
    private readonly string _fingerprint;
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _tcp;
    private SslStream? _ssl;
    private FramedConnection? _conn;

    public event Action<double>? RoundTripMeasured;   // Millisekunden
    public event Action<string>? Disconnected;
    public event Action<byte[]>? VideoData;
    public event Action<int>? VideoReset;            // Monitor
    public event Action<int, int>? VideoInfo;        // Breite, Höhe
    public event Action<string>? VideoError;

    public string ServerName { get; private set; } = "";
    public string ServerFingerprint { get; private set; } = "";

    public SessionClient(X509Certificate2 cert)
    {
        _cert = cert;
        _fingerprint = Identity.Fingerprint(cert);
    }

    /// <param name="expectedFingerprint">Bekannter Fingerabdruck dieses Servers oder null beim ersten Mal.</param>
    /// <param name="askCode">Fragt den Kopplungscode ab (läuft im UI-Thread); null = abgebrochen.</param>
    public async Task ConnectAsync(IPAddress address, string? expectedFingerprint, Func<string, Task<string?>> askCode)
    {
        var ct = _cts.Token;
        _tcp = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _tcp.ConnectAsync(address, Protocol.SessionPort, connectTimeout.Token);
        }

        _ssl = new SslStream(_tcp.GetStream(), false);
        await _ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "ShackRemote",
            ClientCertificates = new X509CertificateCollection { _cert },
            // Eigenes Zertifikat immer senden, auch wenn der Server keine passende CA-Liste anbietet.
            LocalCertificateSelectionCallback = (_, _, _, _, _) => _cert,
            RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is not null,
        }, ct);

        ServerFingerprint = Identity.Fingerprint(_ssl.RemoteCertificate!);
        if (expectedFingerprint is not null && !string.Equals(expectedFingerprint, ServerFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException(
                "Der Server meldet sich mit einem anderen Zertifikat als beim letzten Mal. Verbindung aus Sicherheitsgründen abgebrochen.");

        _conn = new FramedConnection(_ssl);
        await _conn.SendControlAsync(new ControlMessage { Type = "Hello", Name = Environment.MachineName, Version = Protocol.Version }, ct);

        var m = await _conn.ReadControlAsync(ct);
        if (m.Type == "PairRequired")
        {
            var code = await askCode(Identity.Short(ServerFingerprint));
            if (string.IsNullOrWhiteSpace(code))
                throw new OperationCanceledException("Kopplung abgebrochen.");
            await _conn.SendControlAsync(new ControlMessage
            {
                Type = "PairProof",
                Mac = Pairing.Proof(code.Replace(" ", "").Trim(), _fingerprint, ServerFingerprint),
            }, ct);
            var result = await _conn.ReadControlAsync(ct);
            if (!result.Ok)
                throw new AuthenticationException(result.Text ?? "Kopplung fehlgeschlagen.");
            m = await _conn.ReadControlAsync(ct);
        }

        if (m.Type != "Welcome")
            throw new InvalidDataException($"Unerwartete Antwort: {m.Type}");
        ServerName = m.Name ?? address.ToString();

        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(PingLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (type, payload) = await _conn!.ReadAsync(ct);
                if (type == MsgType.Video)
                {
                    VideoData?.Invoke(payload);
                    continue;
                }
                if (type != MsgType.Control) continue;
                var m = System.Text.Json.JsonSerializer.Deserialize<ControlMessage>(payload);
                switch (m?.Type)
                {
                    case "Pong": RoundTripMeasured?.Invoke(Stopwatch.GetElapsedTime(m.T).TotalMilliseconds); break;
                    case "VideoReset": VideoReset?.Invoke(m.Monitor); break;
                    case "VideoInfo": VideoInfo?.Invoke(m.Width, m.Height); break;
                    case "VideoError": VideoError?.Invoke(m.Text ?? "unbekannter Fehler"); break;
                }
            }
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Disconnected?.Invoke(ex.Message);
        }
    }

    private async Task PingLoopAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _conn!.SendControlAsync(new ControlMessage { Type = "Ping", T = Stopwatch.GetTimestamp() }, ct);
                await Task.Delay(1000, ct);
            }
        }
        catch (Exception)
        {
            // Ende wird über die Leseschleife gemeldet.
        }
    }

    public Task StartVideoAsync(int monitor) =>
        _conn!.SendControlAsync(new ControlMessage { Type = "StartVideo", Monitor = monitor }, _cts.Token);

    public Task StopVideoAsync() =>
        _conn!.SendControlAsync(new ControlMessage { Type = "StopVideo" }, _cts.Token);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_ssl is not null) await _ssl.DisposeAsync();
        _tcp?.Dispose();
    }
}
