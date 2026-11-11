using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ShackRemote.Core.Service;

/// <summary>
/// Dateizugriffe des Clients laufen IMMER im Namen des am Shack-PC angemeldeten Benutzers – auch im Dienstbetrieb
/// (dort läuft der Server als SYSTEM und dürfte sonst an alle Dateien).
/// </summary>
public static class UserContext
{
    public static T Run<T>(Func<T> action)
    {
        if (!Desktop.IsSystem) return action();
        if (!WTSQueryUserToken(ProcessLauncher.ActiveConsoleSession(), out var token))
            throw new UnauthorizedAccessException("Am Shack-PC ist niemand angemeldet – Dateizugriff erst nach der Anmeldung.");
        using var handle = new SafeAccessTokenHandle(token);
        return WindowsIdentity.RunImpersonated(handle, action);
    }

    public static void Run(Action action) => Run(() => { action(); return 0; });

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);
}
