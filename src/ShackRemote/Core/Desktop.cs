using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ShackRemote.Core;

/// <summary>
/// Windows-Desktops: normaler Desktop ("Default") und gesicherter Anmelde-/Sperrbildschirm ("Winlogon").
/// Aufnahme und Eingabe müssen im Thread an den Desktop gebunden sein, der gerade die Eingabe hat.
/// Den Winlogon-Desktop darf nur ein Programm mit SYSTEM-Rechten öffnen (Dienstbetrieb).
/// </summary>
public static class Desktop
{
    private const uint GENERIC_ALL = 0x10000000;
    private const int UOI_NAME = 2;

    /// <summary>Läuft dieser Prozess als SYSTEM (also vom Dienst gestartet)?</summary>
    public static bool IsSystem { get; } = WindowsIdentity.GetCurrent().IsSystem;

    /// <summary>Name des Desktops, der gerade die Eingabe hat; null, wenn er nicht geöffnet werden darf (z. B. gesperrt ohne Dienst).</summary>
    public static string? InputDesktopName()
    {
        var h = OpenInputDesktop(0, false, 0x0001 /* DESKTOP_READOBJECTS */);
        if (h == IntPtr.Zero) return null;
        try { return Name(h); }
        finally { CloseDesktop(h); }
    }

    /// <summary>Name des Desktops, an den der aktuelle Thread gebunden ist.</summary>
    public static string? ThreadDesktopName() => Name(GetThreadDesktop(GetCurrentThreadId()));

    /// <summary>Stimmt der Desktop des Threads mit dem Eingabe-Desktop überein?</summary>
    public static bool ThreadIsOnInputDesktop()
    {
        var input = InputDesktopName();
        return input is not null && string.Equals(input, ThreadDesktopName(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bindet den aktuellen Thread an den Eingabe-Desktop. Geht nur, wenn der Thread noch keine Fenster
    /// oder Haken besitzt (eigene Arbeits-Threads). Rückgabe: Name des Desktops oder null.
    /// </summary>
    public static string? AttachToInputDesktop()
    {
        var h = OpenInputDesktop(0, false, GENERIC_ALL);
        if (h == IntPtr.Zero) return null;
        if (!SetThreadDesktop(h))
        {
            CloseDesktop(h);
            return null;
        }
        // Handle bleibt offen, solange der Thread daran gebunden ist (Windows schließt es mit dem Thread nicht selbst,
        // ein kleines Leck je Wechsel ist hier unkritisch).
        return Name(h);
    }

    private static string? Name(IntPtr desktop)
    {
        if (desktop == IntPtr.Zero) return null;
        var buffer = new char[256];
        return GetUserObjectInformation(desktop, UOI_NAME, buffer, (uint)(buffer.Length * 2), out _)
            ? new string(buffer).TrimEnd('\0')
            : null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] char[] pvInfo, uint nLength, out uint lpnLengthNeeded);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
