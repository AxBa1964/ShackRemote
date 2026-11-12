namespace ShackRemote.Core.Service;

/// <summary>
/// Einfaches Protokoll in Dateien unter C:\ProgramData\ShackRemote (dienst.log, agent.log),
/// weil Dienst und Agent sonst unsichtbar arbeiten. Ab 2 MB wird die Datei als *.old.log weggelegt.
/// </summary>
public static class FileLog
{
    private static readonly object Gate = new();

    /// <summary>Ablageort der Protokolle (C:\ProgramData\ShackRemote).</summary>
    public static string Folder => ServiceSetup.DataFolder;

    /// <summary>Hängt eine Zeile mit Zeitstempel an &lt;name&gt;.log an; Fehler beim Schreiben werden ignoriert.</summary>
    public static void Write(string name, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, name + ".log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, Path.Combine(Folder, name + ".old.log"), overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Protokoll ist Hilfe, kein Muss.
        }
    }
}
