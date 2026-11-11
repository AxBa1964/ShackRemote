using System.ServiceProcess;
using ShackRemote.Core;
using ShackRemote.Core.Service;
using ShackRemote.UI;

namespace ShackRemote;

internal static class Program
{
    /// <summary>
    /// Startparameter:
    ///   /server               - direkt als Server starten (Shack-PC)
    ///   /client               - direkt als Client starten (Laptop)
    ///   /rolle                - Rollenauswahl erzwingen, auch wenn "nicht mehr fragen" gesetzt ist
    ///   /dienst               - läuft als Windows-Dienst (wird von Windows so gestartet, nicht von Hand)
    ///   /agent                - Server-Agent als SYSTEM in der Benutzersitzung (startet der Dienst)
    ///   /dienst-installieren  - Dienst einrichten (braucht Adminrechte; Knopf im Server-Fenster)
    ///   /dienst-entfernen     - Dienst entfernen (braucht Adminrechte; Knopf im Server-Fenster)
    ///   /still                - zusammen mit den beiden oberen: ohne Erfolgsmeldung (für das Setup)
    ///   /encoder=X            - Test: Encoder erzwingen (qsv, nvenc, amf, cpu), ohne Rückfall
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        bool Has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));

        if (Has("/dienst"))
        {
            ServiceBase.Run(new ShackService());
            return 0;
        }

        ApplicationConfiguration.Initialize();

        if (Has("/dienst-installieren") || Has("/dienst-entfernen"))
        {
            bool install = Has("/dienst-installieren");
            try
            {
                var result = install ? ServiceSetup.Install() : ServiceSetup.Uninstall();
                if (!Has("/still"))   // /still: aus dem Setup heraus, ohne Meldung
                    MessageBox.Show(result, install ? "ShackRemote – Dienst eingerichtet" : "ShackRemote – Dienst entfernt",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!Has("/still") || install)
                    MessageBox.Show(ex.Message, "ShackRemote – Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // Test: Encoder erzwingen, z. B. /encoder=nvenc
        var encoderArg = args.FirstOrDefault(a => a.StartsWith("/encoder=", StringComparison.OrdinalIgnoreCase));
        if (encoderArg is not null)
            ShackRemote.Core.Media.ScreenEncoder.Forced = ShackRemote.Core.Media.ScreenEncoder.Parse(encoderArg["/encoder=".Length..]);

        bool agent = Has("/agent");
        if (agent)
            AppSettings.UseMachineFolder(ServiceSetup.DataFolder);

        // Darstellung (System/Hell/Dunkel) vor dem ersten Fenster festlegen
        Theme.Init(Theme.Parse(AppSettings.Read(s => s.Design)));

        // Startfenster mit Lizenzhinweis – nicht im Dienstbetrieb
        if (!agent)
        {
            using var intro = new IntroForm();
            intro.ShowDialog();
        }

        string? role = agent || Has("/server") ? "server"
                     : Has("/client") ? "client"
                     : (!Has("/rolle") && AppSettings.Read(s => s.RememberRole)) ? AppSettings.Read(s => s.Role)
                     : null;

        if (role is null)
        {
            using var dlg = new RoleForm();
            if (dlg.ShowDialog() != DialogResult.OK || dlg.Role is null)
                return 0;
            role = dlg.Role;
            var remember = dlg.Remember;
            AppSettings.Update(s => { s.Role = role; s.RememberRole = remember; });
        }

        System.Security.Cryptography.X509Certificates.X509Certificate2 cert;
        try
        {
            cert = Identity.LoadOrCreate();
        }
        catch (Exception ex)
        {
            if (agent) FileLog.Write("agent", $"Zertifikat: {ex.Message}");
            else MessageBox.Show($"Das Zertifikat konnte nicht geladen oder erzeugt werden:\n\n{ex.Message}",
                "ShackRemote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        var mode = agent ? ServerMode.Agent
                 : role == "server" && ServiceSetup.IsRunning ? ServerMode.Management
                 : ServerMode.Normal;
        Form main = role == "server" ? new ServerForm(cert, mode) : new ClientForm(cert);
        Application.Run(main);
        return 0;
    }
}
