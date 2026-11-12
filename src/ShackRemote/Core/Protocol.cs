using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShackRemote.Core;

/// <summary>Feste Werte des Protokolls: Ports, Suchkennung, Programmversion.</summary>
public static class Protocol
{
    /// <summary>UDP: Server-Suche.</summary>
    public const int DiscoveryPort = 47100;

    /// <summary>TCP: Sitzung (TLS).</summary>
    public const int SessionPort = 47101;

    /// <summary>Inhalt einer Suchanfrage per UDP; die Versionsendung erlaubt spätere Änderungen.</summary>
    public const string DiscoverRequest = "SHACKREMOTE_DISCOVER_V1";

    /// <summary>Programmversion aus der Projektdatei (z. B. "1.2.0").</summary>
    public static string Version { get; } =
        typeof(Protocol).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>Kanäle innerhalb der TLS-Verbindung. Jede Nachricht: [Typ 1 Byte][Länge 4 Byte LE][Inhalt].</summary>
public enum MsgType : byte
{
    Control = 1,
    Video = 2,
    Audio = 3,
    Input = 4,
    File = 5,
}

/// <summary>
/// Steuernachricht (JSON). Type: Hello, PairRequired, PairProof, PairResult, Welcome (Monitors), Ping, Pong,
/// StartVideo (Monitor = DXGI-Ausgang), StopVideo, StartAudio (Text = Geräte-Id), StopAudio, RequestKeyframe, VideoReset (Monitor),
/// VideoInfo (Width, Height, Text = Encoder), VideoError, Monitors (geänderte Monitorliste), SendSas, Lock, Info (Text).
/// </summary>
public sealed class ControlMessage
{
    /// <summary>Art der Nachricht (siehe Klassenbeschreibung).</summary>
    public string Type { get; set; } = "";
    /// <summary>Rechnername (Hello, Welcome).</summary>
    public string? Name { get; set; }
    /// <summary>Programmversion (Hello, Welcome).</summary>
    public string? Version { get; set; }
    /// <summary>Kopplungsnachweis: HMAC-SHA256 über beide Fingerabdrücke mit dem Code als Schlüssel.</summary>
    public string? Mac { get; set; }
    /// <summary>Ergebnis, z. B. bei PairResult.</summary>
    public bool Ok { get; set; }
    /// <summary>Freitext: Fehlermeldung, Geräte-Id, Encoder-Name oder Rückmeldung.</summary>
    public string? Text { get; set; }
    /// <summary>Zeitstempel (Stopwatch-Ticks) für Ping/Pong; misst die Umlaufzeit.</summary>
    public long T { get; set; }
    /// <summary>DXGI-Ausgangsnummer (MonitorInfo.Index) bei StartVideo/VideoReset.</summary>
    public int Monitor { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Monitore des Servers (Welcome, Monitors).</summary>
    public List<MonitorInfo>? Monitors { get; set; }
    /// <summary>Tonquellen des Servers (Welcome).</summary>
    public List<AudioDevice>? AudioDevices { get; set; }
}

/// <summary>Tonquelle des Servers. Id "" = Standard-Wiedergabegerät (Loopback). Kind: "Wiedergabe" oder "Aufnahme".</summary>
public sealed class AudioDevice
{
    /// <summary>WASAPI-Geräte-Id; "" = Standard-Wiedergabegerät.</summary>
    public string Id { get; set; } = "";
    /// <summary>Anzeigename des Geräts.</summary>
    public string Name { get; set; } = "";
    /// <summary>"Wiedergabe" (Loopback) oder "Aufnahme".</summary>
    public string Kind { get; set; } = "";
}

/// <summary>
/// Ein Bildschirm des Servers. Index = laufende Nummer über alle Grafikkarten (Kennung im Protokoll),
/// Adapter/Output = DXGI-Grafikkarte und -Ausgang (für ddagrab), Display = Windows-interne Anzeigenummer.
/// </summary>
public sealed class MonitorInfo
{
    public int Index { get; set; }
    public int Adapter { get; set; }
    public int Output { get; set; }
    public uint Vendor { get; set; }
    /// <summary>Name der Grafikkarte.</summary>
    public string AdapterName { get; set; } = "";
    public int Display { get; set; }
    /// <summary>Windows-Gerätename des Monitors (z. B. \\.\DISPLAY1).</summary>
    public string DeviceName { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Hauptbildschirm von Windows.</summary>
    public bool Primary { get; set; }
}

/// <summary>Video-Nachricht: [1 Byte Merker][ein H.264-Paket]. Merker Bit 0 = Schlüsselbild.</summary>
public static class VideoPacket
{
    /// <summary>Merker-Bit: Paket ist ein Schlüsselbild.</summary>
    public const byte KeyFlag = 1;

    /// <summary>Video-Nachricht aus H.264-Paket und Schlüsselbild-Merker bauen.</summary>
    public static byte[] Build(byte[] data, bool key)
    {
        var payload = new byte[data.Length + 1];
        payload[0] = key ? KeyFlag : (byte)0;
        data.CopyTo(payload, 1);
        return payload;
    }
}

/// <summary>Antwort auf die Server-Suche.</summary>
public sealed class ServerInfo
{
    /// <summary>Rechnername des Servers.</summary>
    public string Name { get; set; } = "";
    /// <summary>Programmversion des Servers.</summary>
    public string Version { get; set; } = "";
    /// <summary>Fingerabdruck des Server-Zertifikats.</summary>
    public string Fingerprint { get; set; } = "";
    public int Port { get; set; }
    /// <summary>Adressen, auf denen der Server lauscht.</summary>
    public List<AdvertisedAddress> Addresses { get; set; } = new();
}

/// <summary>Eine Adresse, auf der der Server lauscht, mit Netzart (LAN, WLAN, Tailscale).</summary>
public sealed class AdvertisedAddress
{
    /// <summary>IPv4-Adresse.</summary>
    public string Address { get; set; } = "";
    /// <summary>Netzart (LAN, WLAN, Tailscale, Sonstige).</summary>
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

    /// <summary>Sendet eine Nachricht [Typ][Länge][Inhalt]; threadsicher (mehrere Kanäle senden parallel).</summary>
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

    /// <summary>Sendet eine Steuernachricht als JSON.</summary>
    public Task SendControlAsync(ControlMessage message, CancellationToken ct = default) =>
        SendAsync(MsgType.Control, JsonSerializer.SerializeToUtf8Bytes(message), ct);

    /// <summary>Liest die nächste Nachricht; nur aus einem Thread aufrufen.</summary>
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

    /// <summary>Liest die nächste Nachricht und erwartet eine Steuernachricht.</summary>
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
    /// <summary>Neuer zufälliger 6-stelliger Kopplungscode.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>Kopplungsnachweis aus Code und beiden Fingerabdrücken (HMAC-SHA256, hexadezimal).</summary>
    public static string Proof(string code, string clientFingerprint, string serverFingerprint)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(clientFingerprint + "|" + serverFingerprint)));
    }

    /// <summary>Vergleicht Nachweise in konstanter Zeit (kein Rückschluss über die Laufzeit).</summary>
    public static bool Matches(string expected, string? given) =>
        given is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given));
}
