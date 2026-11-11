using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ShackRemote.Core.Service;

/// <summary>
/// Einrichten und Entfernen des Dienstes – aus dem Server-Fenster per Knopf (mit UAC-Abfrage), ähnlich XAMPP.
/// Einrichten:
///  1. Programmdateien nach C:\Program Files\ShackRemote kopieren (der Dienst läuft von dort),
///  2. Kopplungen und Zertifikat des Benutzers nach C:\ProgramData\ShackRemote übernehmen
///     (Laptop bleibt gekoppelt; Ordner nur für SYSTEM und Administratoren lesbar),
///  3. Richtlinie SoftwareSASGeneration setzen, damit Dienste Strg+Alt+Entf auslösen dürfen,
///  4. Dienst anlegen (Autostart, Neustart bei Absturz) und starten.
/// </summary>
public static class ServiceSetup
{
    public static string ProgramFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ShackRemote");

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ShackRemote");

    /// <summary>Zustand für die Anzeige: null = nicht eingerichtet.</summary>
    public static ServiceControllerStatus? Status()
    {
        try
        {
            using var sc = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ShackService.Name);
            return sc?.Status;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRunning => Status() == ServiceControllerStatus.Running;

    /// <summary>Startet dieses Programm mit Adminrechten (UAC-Abfrage) für /dienst-installieren bzw. /dienst-entfernen.</summary>
    public static int RunElevated(string argument)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!, argument) { UseShellExecute = true, Verb = "runas" };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Start mit Adminrechten nicht möglich.");
        p.WaitForExit();
        return p.ExitCode;
    }

    // ---- Läuft mit Adminrechten ------------------------------------------------------------------

    public static string Install()
    {
        var log = new StringBuilder();

        // Laufenden Dienst anhalten, sonst sind die Dateien gesperrt
        StopService(log);

        // 1. Programmdateien kopieren
        var source = AppContext.BaseDirectory.TrimEnd('\\');
        var target = ProgramFolder.TrimEnd('\\');
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            CopyFolder(source, target);
            log.AppendLine($"Programmdateien kopiert nach {target}");
        }
        else
        {
            log.AppendLine($"Programm läuft bereits aus {target}");
        }

        // 2. Daten des Benutzers übernehmen (nur wenn dort noch nichts liegt)
        Directory.CreateDirectory(DataFolder);
        var userFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShackRemote");
        foreach (var file in new[] { "identity.pfx", "settings.json" })
        {
            var from = Path.Combine(userFolder, file);
            var to = Path.Combine(DataFolder, file);
            if (File.Exists(from) && !File.Exists(to))
            {
                File.Copy(from, to);
                log.AppendLine($"{file} übernommen (Kopplungen bleiben gültig)");
            }
        }
        // Nur SYSTEM (S-1-5-18) und Administratoren (S-1-5-32-544): der private Schlüssel liegt hier
        Run("icacls.exe", log, DataFolder, "/inheritance:r", "/grant:r", "*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F");

        // 3. Strg+Alt+Entf durch Dienste erlauben (Richtlinie "Software Secure Attention Sequence")
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true))
        {
            int current = key.GetValue("SoftwareSASGeneration") is int v ? v : 0;
            int wanted = current switch { 0 => 1, 2 => 3, _ => current };   // 1 = Dienste, 3 = Dienste + Erleichterte Bedienung
            if (wanted != current)
            {
                key.SetValue("SoftwareSASGeneration", wanted, RegistryValueKind.DWord);
                log.AppendLine($"Richtlinie SoftwareSASGeneration = {wanted} gesetzt (Strg+Alt+Entf durch Dienst)");
            }
        }

        // 4. Dienst anlegen bzw. Pfad aktualisieren, Neustart bei Absturz, starten
        var exe = Path.Combine(target, "ShackRemote.exe");
        var binPath = $"\"{exe}\" /dienst";
        if (Status() is null)
            Run("sc.exe", log, "create", ShackService.Name, "binPath=", binPath, "start=", "auto", "DisplayName=", "ShackRemote Server");
        else
            Run("sc.exe", log, "config", ShackService.Name, "binPath=", binPath, "start=", "auto");
        Run("sc.exe", log, "description", ShackService.Name, "Fernsteuerung des Shack-PCs (ShackRemote): Bild, Maus, Tastatur, auch Anmeldebildschirm.");
        Run("sc.exe", log, "failure", ShackService.Name, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/30000");

        using (var sc = new ServiceController(ShackService.Name))
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }
        log.AppendLine("Dienst läuft. Er startet ab jetzt automatisch mit Windows.");
        return log.ToString();
    }

    public static string Uninstall()
    {
        var log = new StringBuilder();
        StopService(log);
        if (Status() is not null)
        {
            Run("sc.exe", log, "delete", ShackService.Name);
            log.AppendLine("Dienst entfernt.");
        }
        log.AppendLine($"Programmdateien ({ProgramFolder}) und Daten ({DataFolder}) bleiben liegen.");
        return log.ToString();
    }

    private static void StopService(StringBuilder log)
    {
        if (Status() is not ServiceControllerStatus status || status == ServiceControllerStatus.Stopped) return;
        using var sc = new ServiceController(ShackService.Name);
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        log.AppendLine("Laufender Dienst angehalten.");
    }

    private static void CopyFolder(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static void Run(string exe, StringBuilder log, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{exe} {string.Join(" ", args)} meldet Fehler {p.ExitCode}: {output.Trim()}");
    }
}
