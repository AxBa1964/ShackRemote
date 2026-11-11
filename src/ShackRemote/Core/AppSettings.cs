using System.Text.Json;

namespace ShackRemote.Core;

/// <summary>
/// Einstellungen in %APPDATA%\ShackRemote\settings.json.
/// Zugriff nur über Read/Update, weil Netzwerk-Threads und Oberfläche gleichzeitig zugreifen.
/// </summary>
public sealed class AppSettings
{
    public string? Role { get; set; }

    /// <summary>Darstellung: "System", "Hell" oder "Dunkel".</summary>
    public string? Design { get; set; } = "System";
    public bool RememberRole { get; set; }

    /// <summary>Server: IPv4-Adressen, auf denen NICHT gelauscht werden soll.</summary>
    public List<string> ExcludedAddresses { get; set; } = new();

    /// <summary>Server: gekoppelte Clients.</summary>
    public List<TrustedClient> TrustedClients { get; set; } = new();

    /// <summary>Client: gewählte Tonquelle beim Server (Geräte-Id, "" = Standard-Wiedergabe, null = Ton aus).</summary>
    public string? AudioSource { get; set; } = "";

    /// <summary>Client: zuletzt geöffnete Ordner im Dateifenster.</summary>
    public string? FileLocalPath { get; set; }
    public string? FileRemotePath { get; set; }

    /// <summary>Client: bekannte (gekoppelte) Server.</summary>
    public List<KnownServer> KnownServers { get; set; } = new();

    /// <summary>
    /// Normal %APPDATA%\ShackRemote. Im Dienstbetrieb (Agent als SYSTEM) C:\ProgramData\ShackRemote,
    /// siehe <see cref="UseMachineFolder"/>.
    /// </summary>
    public static string Folder { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShackRemote");

    /// <summary>Auf den rechnerweiten Datenordner umschalten (vor dem Laden des Zertifikats aufrufen).</summary>
    public static void UseMachineFolder(string folder)
    {
        lock (Gate)
        {
            Folder = folder;
            Current = Load();
        }
    }

    private static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly object Gate = new();
    private static AppSettings Current = Load();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static T Read<T>(Func<AppSettings, T> read)
    {
        lock (Gate) return read(Current);
    }

    public static void Update(Action<AppSettings> change)
    {
        lock (Gate)
        {
            // Vor jeder Änderung neu laden: Server und Client können auf demselben Rechner parallel laufen.
            Current = Load();
            change(Current);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Beschädigte Datei: mit Standardwerten weitermachen, Datei wird beim nächsten Speichern ersetzt.
        }
        return new AppSettings();
    }
}

public sealed class TrustedClient
{
    public string Fingerprint { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime PairedAt { get; set; }
}

public sealed class KnownServer
{
    public string Fingerprint { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Addresses { get; set; } = new();
    public DateTime LastUsed { get; set; }
}
