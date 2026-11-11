using System.Runtime.InteropServices;

namespace ShackRemote.Core.Service;

/// <summary>Befehle des Clients, die Windows-Sonderrechte brauchen: Strg+Alt+Entf und Bildschirm sperren.</summary>
public static class SystemActions
{
    /// <summary>Strg+Alt+Entf: Der Agent bittet den Dienst darum (SendSAS ist Diensten vorbehalten).</summary>
    public static string SendCtrlAltDel()
    {
        if (!Desktop.IsSystem)
            return "Strg+Alt+Entf geht nur mit eingerichtetem Dienst (Server-Fenster: \"Dienst einrichten\").";
        if (!EventWaitHandle.TryOpenExisting(ShackService.SasEventName, out var ev))
            return "Strg+Alt+Entf: Dienst nicht erreichbar.";
        using (ev) ev.Set();
        return "Strg+Alt+Entf ausgelöst.";
    }

    /// <summary>Shack-PC sperren (wie Win+L dort).</summary>
    public static string Lock()
    {
        if (Desktop.IsSystem)
        {
            // Sperren muss im Namen des angemeldeten Benutzers geschehen
            var rundll = Path.Combine(Environment.SystemDirectory, "rundll32.exe");
            using var p = ProcessLauncher.StartAsSessionUser(rundll, "user32.dll,LockWorkStation", ProcessLauncher.ActiveConsoleSession());
            return "Shack-PC gesperrt.";
        }
        return LockWorkStation() ? "Shack-PC gesperrt." : $"Sperren fehlgeschlagen (Fehler {Marshal.GetLastWin32Error()}).";
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
