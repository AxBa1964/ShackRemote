using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShackRemote.Core.Service;

/// <summary>
/// Startet Prozesse in einer bestimmten Windows-Sitzung – wie bei Fernsteuerungsprogrammen üblich:
///  - als SYSTEM in der Konsolensitzung (Server-Agent; darf auf den Anmelde-/Sperrbildschirm),
///  - als angemeldeter Benutzer (z. B. zum Sperren des Bildschirms).
/// Benötigt SYSTEM-Rechte (Dienst bzw. vom Dienst gestarteter Agent).
/// </summary>
public static class ProcessLauncher
{
    public const uint NoSession = 0xFFFFFFFF;

    /// <summary>Sitzung, die gerade an Bildschirm/Tastatur des PCs hängt.</summary>
    public static uint ActiveConsoleSession() => WTSGetActiveConsoleSessionId();

    /// <summary>Eigenes (SYSTEM-)Token kopieren, in die Sitzung verlegen und Prozess darin starten.</summary>
    public static Process StartAsSystemInSession(string exe, string arguments, uint session)
    {
        const uint TOKEN_ASSIGN_PRIMARY = 0x0001, TOKEN_DUPLICATE = 0x0002, TOKEN_QUERY = 0x0008,
                   TOKEN_ADJUST_DEFAULT = 0x0080, TOKEN_ADJUST_SESSIONID = 0x0100;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
                TOKEN_ASSIGN_PRIMARY | TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID, out var own))
            throw Error("OpenProcessToken");
        try
        {
            if (!DuplicateTokenEx(own, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out var token))
                throw Error("DuplicateTokenEx");
            try
            {
                if (!SetTokenInformation(token, TokenSessionId, ref session, sizeof(uint)))
                    throw Error("SetTokenInformation(Sitzung)");
                return Start(token, exe, arguments, withUserEnvironment: false);
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(own); }
    }

    /// <summary>Prozess als der in der Sitzung angemeldete Benutzer starten.</summary>
    public static Process StartAsSessionUser(string exe, string arguments, uint session)
    {
        if (!WTSQueryUserToken(session, out var user))
            throw Error("WTSQueryUserToken (ist jemand angemeldet?)");
        try
        {
            if (!DuplicateTokenEx(user, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out var token))
                throw Error("DuplicateTokenEx");
            try { return Start(token, exe, arguments, withUserEnvironment: true); }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(user); }
    }

    private static Process Start(IntPtr token, string exe, string arguments, bool withUserEnvironment)
    {
        const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400, CREATE_NO_WINDOW = 0x08000000;
        IntPtr env = IntPtr.Zero;
        if (withUserEnvironment) CreateEnvironmentBlock(out env, token, false);
        try
        {
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            // Befehlszeile muss beschreibbar sein (CreateProcessAsUserW darf sie ändern)
            var cmd = new System.Text.StringBuilder($"\"{exe}\" {arguments}");
            uint flags = (env != IntPtr.Zero ? CREATE_UNICODE_ENVIRONMENT : 0) | (withUserEnvironment ? CREATE_NO_WINDOW : 0);
            if (!CreateProcessAsUser(token, exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, env,
                    Path.GetDirectoryName(exe), ref si, out var pi))
                throw Error("CreateProcessAsUser");
            CloseHandle(pi.hThread);
            try { return Process.GetProcessById((int)pi.dwProcessId); }
            finally { CloseHandle(pi.hProcess); }
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
        }
    }

    private static Exception Error(string what) =>
        new InvalidOperationException($"{what} fehlgeschlagen (Fehler {Marshal.GetLastWin32Error()})");

    // ---- Win32 ------------------------------------------------------------------------------------

    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const int SecurityImpersonation = 2, TokenPrimary = 1, TokenSessionId = 12;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref uint info, int length);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessAsUserW")]
    private static extern bool CreateProcessAsUser(IntPtr token, string? application, System.Text.StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
