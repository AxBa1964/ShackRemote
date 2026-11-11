using ShackRemote.Core;
using ShackRemote.UI;

namespace ShackRemote;

internal static class Program
{
    /// <summary>
    /// Startparameter:
    ///   /server  - direkt als Server starten (Shack-PC)
    ///   /client  - direkt als Client starten (Laptop)
    ///   /rolle   - Rollenauswahl erzwingen, auch wenn "nicht mehr fragen" gesetzt ist
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool Has(string a) => args.Any(x => x.Equals(a, StringComparison.OrdinalIgnoreCase));

        string? role = Has("/server") ? "server"
                     : Has("/client") ? "client"
                     : (!Has("/rolle") && AppSettings.Read(s => s.RememberRole)) ? AppSettings.Read(s => s.Role)
                     : null;

        if (role is null)
        {
            using var dlg = new RoleForm();
            if (dlg.ShowDialog() != DialogResult.OK || dlg.Role is null)
                return;
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
            MessageBox.Show($"Das Zertifikat konnte nicht geladen oder erzeugt werden:\n\n{ex.Message}",
                "ShackRemote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Form main = role == "server" ? new ServerForm(cert) : new ClientForm(cert);
        Application.Run(main);
    }
}
