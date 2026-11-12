using System.Text.Json;

namespace ShackRemote.Core;

/// <summary>
/// Einstellungen in %APPDATA%\ShackRemote\settings.json.
/// Zugriff nur über Read/Update, weil Netzwerk-Threads und Oberfläche gleichzeitig zugreifen.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Client: zuletzt gewählte Rolle ("server" oder "client").</summary>
    public string? Role { get; set; }

    /// <summary>Darstellung: "System", "Hell" oder "Dunkel".</summary>
    public string? Design { get; set; } = "System";
    /// <summary>Rolle beim nächsten Start nicht mehr abfragen (änderbar mit Startparameter /rolle).</summary>
    public bool RememberRole { get; set; }

    /// <summary>Server: IPv4-Adressen, auf denen NICHT gelauscht werden soll.</summary>
    public List<string> ExcludedAddresses { get; set; } = new();

    /// <summary>Server: gekoppelte Clients.</summary>
    public List<TrustedClient> TrustedClients { get; set; } = new();

    /// <summary>Client: gewählte Tonquelle beim Server (Geräte-Id, "" = Standard-Wiedergabe, null = Ton aus).</summary>
    public string? AudioSource { get; set; } = "";

    /// <summary>Client: zuletzt geöffnete Ordner im Dateifenster.</summary>
    public string? FileLocalPath { get; set; }
    /// <summary>Zuletzt geöffneter Ordner auf dem Shack-PC im Dateifenster.</summary>
    public string? FileRemotePath { get; set; }

    /// <summary>Client: bekannte (gekoppelte) Server.</summary>
    public List<KnownServer> KnownServers { get; set; } = new();

    // Reihenfolge wichtig: statische Felder werden von oben nach unten belegt.

    /// <summary>Ordner der Einstellungen des angemeldeten Benutzers (%APPDATA%\ShackRemote).</summary>
    public static string UserFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShackRemote");

    /// <summary>
    /// Normal %APPDATA%\ShackRemote. Im Dienstbetrieb (Agent als SYSTEM) C:\ProgramData\ShackRemote,
    /// siehe <see cref="UseMachineFolder"/>.
    /// </summary>
    public static string Folder { get; private set; } = UserFolder;

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

    /// <summary>Einstellungen ändern und sofort speichern. Lädt vorher neu, weil Server und Client parallel schreiben können.</summary>
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

    private static AppSettings Load() => LoadFrom(Folder) ?? new AppSettings();

    /// <summary>Datei neu einlesen (z. B. wenn ein anderer Prozess eine Kopplung entfernt hat).</summary>
    public static void Reload()
    {
        lock (Gate) Current = Load();
    }

    /// <summary>
    /// Einstellungen aus einem bestimmten Ordner lesen. null = keine Datei, nicht lesbar (Rechte) oder beschädigt.
    /// </summary>
    public static AppSettings? LoadFrom(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "settings.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            // Beschädigt oder keine Leserechte
        }
        return null;
    }

    /// <summary>Einstellungen in einen bestimmten Ordner schreiben (Setup/Dienst-Einrichtung).</summary>
    public static void SaveTo(string folder, AppSettings settings)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "settings.json"), JsonSerializer.Serialize(settings, JsonOptions));
    }

}

/// <summary>Server: ein gekoppeltes Gerät (Fingerabdruck des Client-Zertifikats).</summary>
public sealed class TrustedClient
{
    /// <summary>SHA-256-Fingerabdruck des Zertifikats (hexadezimal).</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>Rechnername zum Zeitpunkt der Kopplung (nur Anzeige).</summary>
    public string Name { get; set; } = "";
    /// <summary>Zeitpunkt der Kopplung.</summary>
    public DateTime PairedAt { get; set; }
}

/// <summary>Client: ein gekoppelter Server mit seinem Zertifikat und den zuletzt bekannten Adressen (für die gezielte Suche).</summary>
public sealed class KnownServer
{
    /// <summary>SHA-256-Fingerabdruck des Server-Zertifikats; wird bei jeder Verbindung geprüft.</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>Rechnername des Servers.</summary>
    public string Name { get; set; } = "";
    /// <summary>Vom Server angebotene Adressen; Ziele der gezielten Suche.</summary>
    public List<string> Addresses { get; set; } = new();
    /// <summary>Letzte erfolgreiche Verbindung.</summary>
    public DateTime LastUsed { get; set; }
}
