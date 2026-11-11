using System.Runtime.InteropServices;

namespace ShackRemote.UI;

/// <summary>
/// Globaler Tastatur-Haken (WH_KEYBOARD_LL) wie bei AnyDesk: Solange das Fernsteuerungsfenster vorne ist,
/// werden ALLE Tasten abgefangen – auch Windows-Taste, Alt+Tab, Alt+F4 – und an den Shack-PC geschickt.
/// Strg+Alt+Entf kann Windows-bedingt nicht abgefangen werden.
/// Muss im UI-Thread installiert werden (der Haken braucht dessen Nachrichtenschleife).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80;

    private readonly HookProc _proc;   // Verweis halten, sonst räumt der GC den Rückruf ab
    private IntPtr _hook;

    /// <summary>Ob gerade abgefangen werden soll (z. B. Fenster ist vorne).</summary>
    public Func<bool> IsActive { get; set; } = () => false;

    /// <summary>Taste: VK, Scancode, losgelassen, erweitert. Rückgabe true = verschlucken (nicht lokal verarbeiten).</summary>
    public Func<ushort, ushort, bool, bool, bool>? KeyEvent { get; set; }

    public KeyboardHook()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Tastatur-Haken nicht möglich (Fehler {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && KeyEvent is not null)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool injected = (data.flags & LLKHF_INJECTED) != 0;
            if (!injected && IsActive())
            {
                bool up = (data.flags & LLKHF_UP) != 0;
                bool extended = (data.flags & LLKHF_EXTENDED) != 0;
                if (KeyEvent((ushort)data.vkCode, (ushort)data.scanCode, up, extended))
                    return 1;   // verschluckt
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
