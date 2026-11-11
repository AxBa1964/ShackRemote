using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShackRemote.Core;

public static class Protocol
{
    /// <summary>UDP: Server-Suche.</summary>
    public const int DiscoveryPort = 47100;

    /// <summary>TCP: Sitzung (TLS).</summary>
    public const int SessionPort = 47101;

    public const string DiscoverRequest = "SHACKREMOTE_DISCOVER_V1";

    public static string Version { get; } =
        typeof(Protocol).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>Kanäle innerhalb der TLS-Verbindung. Video, Audio und Eingabe folgen in Stufe 1b/1c.</summary>
public enum MsgType : byte
{
    Control = 1,
    Video = 2,
    Audio = 3,
    Input = 4,
}

/// <summary>
/// Steuernachricht (JSON). Type: Hello, PairRequired, PairProof, PairResult, Welcome, Ping, Pong,
/// StartVideo (Monitor), StopVideo, VideoReset (Monitor), VideoInfo (Width, Height).
/// </summary>
public sealed class ControlMessage
{
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Mac { get; set; }
    public bool Ok { get; set; }
    public string? Text { get; set; }
    public long T { get; set; }
    public int Monitor { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>Antwort auf die Server-Suche.</summary>
public sealed class ServerInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int Port { get; set; }
    public List<AdvertisedAddress> Addresses { get; set; } = new();
}

public sealed class AdvertisedAddress
{
    public string Address { get; set; } = "";
    public string Kind { get; set; } = "";
}

/// <summary>
/// Nachrichten mit Rahmen: [1 Byte Typ][4 Byte Länge, little endian][Nutzdaten].
/// Schreiben ist threadsicher, Lesen nur aus einer Leseschleife.
/// </summary>
public sealed class FramedConnection
{
    private const int MaxPayload = 64 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FramedConnection(Stream stream) => _stream = stream;

    public async Task SendAsync(MsgType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var header = new byte[5];
        header[0] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), payload.Length);

        await _writeLock.WaitAsync(ct);
        try
        {
            // Bewusst OHNE Abbruch-Token: Ein mitten im Schreiben abgebrochener TLS-Datensatz
            // zerstört die Verbindung ("The decryption operation failed" auf der Gegenseite).
            await _stream.WriteAsync(header, CancellationToken.None);
            if (payload.Length > 0)
                await _stream.WriteAsync(payload, CancellationToken.None);
            await _stream.FlushAsync(CancellationToken.None);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SendControlAsync(ControlMessage message, CancellationToken ct = default) =>
        SendAsync(MsgType.Control, JsonSerializer.SerializeToUtf8Bytes(message), ct);

    public async Task<(MsgType Type, byte[] Payload)> ReadAsync(CancellationToken ct = default)
    {
        var header = new byte[5];
        await _stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (length < 0 || length > MaxPayload)
            throw new InvalidDataException($"Ungültige Nachrichtenlänge {length}.");
        var payload = new byte[length];
        if (length > 0)
            await _stream.ReadExactlyAsync(payload, ct);
        return ((MsgType)header[0], payload);
    }

    public async Task<ControlMessage> ReadControlAsync(CancellationToken ct = default)
    {
        var (type, payload) = await ReadAsync(ct);
        if (type != MsgType.Control)
            throw new InvalidDataException($"Steuernachricht erwartet, {type} erhalten.");
        return JsonSerializer.Deserialize<ControlMessage>(payload)
               ?? throw new InvalidDataException("Leere Steuernachricht.");
    }
}

/// <summary>
/// Kopplung per 6-stelligem Code. Der Nachweis bindet den Code an beide Zertifikate:
/// Sitzt jemand dazwischen, sieht jede Seite andere Fingerabdrücke und der Nachweis passt nicht.
/// Bekannte Grenze: Wer zum Zeitpunkt der Kopplung aktiv im Netz dazwischen sitzt, könnte den
/// Code aus dem Nachweis durchprobieren. Deshalb Kopplung im Heimnetz durchführen.
/// </summary>
public static class Pairing
{
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Proof(string code, string clientFingerprint, string serverFingerprint)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(clientFingerprint + "|" + serverFingerprint)));
    }

    public static bool Matches(string expected, string? given) =>
        given is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given));
}
