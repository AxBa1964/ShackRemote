using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ShackRemote.Core;

/// <summary>
/// Server: führt Maus- und Tastatureingaben des Clients per SendInput aus.
/// Mauspositionen kommen relativ zum gezeigten Monitor (0..65535) und werden auf den
/// gesamten virtuellen Desktop umgerechnet (alle drei Monitore).
/// Grenze von Windows: Fenster, die als Administrator laufen, und UAC-Abfragen nehmen
/// keine Eingaben von einem normal gestarteten Programm an.
/// </summary>
public static class InputInjector
{
    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100,
        MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_VIRTUALDESK = 0x4000,
        MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    private static readonly HashSet<(ushort Vk, ushort Scan, bool Ext)> KeysDown = new();
    private static readonly HashSet<byte> ButtonsDown = new();

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    // Eigener Thread: Er kann (im Dienstbetrieb) an den jeweils aktiven Desktop gebunden werden,
    // damit Eingaben auch auf dem Sperr-/Anmeldebildschirm ankommen.
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = new();
    private static readonly Lazy<Thread> Worker = new(() =>
    {
        var t = new Thread(() =>
        {
            foreach (var action in Queue.GetConsumingEnumerable())
            {
                try
                {
                    if (!Desktop.ThreadIsOnInputDesktop()) Desktop.AttachToInputDesktop();
                    action();
                }
                catch { /* einzelne Eingabe verloren – nicht weiter schlimm */ }
            }
        }) { IsBackground = true, Name = "Eingabe" };
        t.Start();
        return t;
    });

    /// <summary>Eingabe-Nachricht einreihen. monitor = der Bildschirm, den der Client gerade sieht.</summary>
    public static void Apply(byte[] msg, MonitorInfo? monitor)
    {
        _ = Worker.Value;
        Queue.Add(() => ApplyNow(msg, monitor));
    }

    /// <summary>Beim Verbindungsende: alle noch gedrückten Tasten und Maustasten loslassen (sonst "klemmt" z. B. Strg).</summary>
    public static void ReleaseAll()
    {
        _ = Worker.Value;
        Queue.Add(ReleaseAllNow);
    }

    private static void ApplyNow(ReadOnlySpan<byte> msg, MonitorInfo? monitor)
    {
        if (msg.Length == 0) return;
        switch (msg[0])
        {
            case InputProtocol.MouseMove when msg.Length >= 5 && monitor is not null:
                MouseAbsolute(monitor, Read16(msg, 1), Read16(msg, 3), MOUSEEVENTF_MOVE, 0);
                break;

            case InputProtocol.MouseButton when msg.Length >= 7 && monitor is not null:
            {
                bool down = msg[2] != 0;
                (uint flags, uint data) = msg[1] switch
                {
                    0 => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
                    1 => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
                    2 => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
                    3 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, 1u),   // XBUTTON1
                    4 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, 2u),   // XBUTTON2
                    _ => (0u, 0u),
                };
                lock (ButtonsDown) { if (down) ButtonsDown.Add(msg[1]); else ButtonsDown.Remove(msg[1]); }
                if (flags != 0)
                    MouseAbsolute(monitor, Read16(msg, 3), Read16(msg, 5), MOUSEEVENTF_MOVE | flags, data);
                break;
            }

            case InputProtocol.MouseWheel when msg.Length >= 4:
            {
                short delta = BinaryPrimitives.ReadInt16LittleEndian(msg[1..]);
                var input = new INPUT { type = INPUT_MOUSE };
                input.u.mi.mouseData = unchecked((uint)delta);
                input.u.mi.dwFlags = msg[3] != 0 ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;
                Send(input);
                break;
            }

            case InputProtocol.Key when msg.Length >= 6:
            {
                var input = new INPUT { type = INPUT_KEYBOARD };
                input.u.ki.wVk = Read16(msg, 1);
                input.u.ki.wScan = Read16(msg, 3);
                input.u.ki.dwFlags = ((msg[5] & 1) != 0 ? KEYEVENTF_KEYUP : 0) | ((msg[5] & 2) != 0 ? KEYEVENTF_EXTENDEDKEY : 0);
                var key = (input.u.ki.wVk, input.u.ki.wScan, (msg[5] & 2) != 0);
                lock (KeysDown) { if ((msg[5] & 1) != 0) KeysDown.Remove(key); else KeysDown.Add(key); }
                Send(input);
                break;
            }
        }
    }

    private static void ReleaseAllNow()
    {
        List<(ushort Vk, ushort Scan, bool Ext)> keys;
        lock (KeysDown) { keys = KeysDown.ToList(); KeysDown.Clear(); }
        foreach (var (vk, scan, ext) in keys)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = vk;
            input.u.ki.wScan = scan;
            input.u.ki.dwFlags = KEYEVENTF_KEYUP | (ext ? KEYEVENTF_EXTENDEDKEY : 0);
            Send(input);
        }

        List<byte> buttons;
        lock (ButtonsDown) { buttons = ButtonsDown.ToList(); ButtonsDown.Clear(); }
        foreach (var b in buttons)
        {
            uint flags = b switch
            {
                0 => MOUSEEVENTF_LEFTUP, 1 => MOUSEEVENTF_RIGHTUP, 2 => MOUSEEVENTF_MIDDLEUP,
                3 or 4 => MOUSEEVENTF_XUP, _ => 0,
            };
            if (flags == 0) continue;
            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = flags;
            input.u.mi.mouseData = b == 3 ? 1u : b == 4 ? 2u : 0u;
            Send(input);
        }
    }

    private static void MouseAbsolute(MonitorInfo m, ushort nx, ushort ny, uint flags, uint data)
    {
        // Pixel auf dem Monitor -> Pixel im virtuellen Desktop -> 0..65535 über den ganzen virtuellen Desktop
        int px = m.Left + (int)((long)nx * Math.Max(1, m.Width - 1) / 65535);
        int py = m.Top + (int)((long)ny * Math.Max(1, m.Height - 1) / 65535);

        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN)), vh = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        var input = new INPUT { type = INPUT_MOUSE };
        input.u.mi.dx = (int)Math.Round((px - vx) * 65535.0 / (vw - 1));
        input.u.mi.dy = (int)Math.Round((py - vy) * 65535.0 / (vh - 1));
        input.u.mi.mouseData = data;
        input.u.mi.dwFlags = flags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
        Send(input);
    }

    private static ushort Read16(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(s[offset..]);

    private static void Send(INPUT input)
    {
        var inputs = new[] { input };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    // ---- Win32 ------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
