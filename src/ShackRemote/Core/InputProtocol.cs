using System.Buffers.Binary;

namespace ShackRemote.Core;

/// <summary>
/// Eingabe-Nachrichten (MsgType.Input), binär und kurz:
///   1 Mausbewegung: [1][x u16][y u16]                         x/y = 0..65535 relativ zum gezeigten Monitor
///   2 Maustaste:    [2][Taste][1=gedrückt/0=los][x u16][y u16]  Taste: 0 links, 1 rechts, 2 Mitte, 3 X1, 4 X2
///   3 Mausrad:      [3][Delta i16][1=waagrecht]                 Delta wie WM_MOUSEWHEEL (120 je Raste)
///   4 Taste:        [4][VK u16][Scancode u16][Merker]          Merker Bit 0 = losgelassen, Bit 1 = erweiterte Taste
/// </summary>
public static class InputProtocol
{
    /// <summary>Nachrichtentypen (erstes Byte einer Input-Nachricht).</summary>
    public const byte MouseMove = 1, MouseButton = 2, MouseWheel = 3, Key = 4;

    /// <summary>Mausbewegung; x/y 0..65535 relativ zum gezeigten Monitor.</summary>
    public static byte[] Move(ushort x, ushort y)
    {
        var b = new byte[5];
        b[0] = MouseMove;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), x);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(3), y);
        return b;
    }

    /// <summary>Maustaste (0 links, 1 rechts, 2 Mitte, 3/4 Seitentasten) gedrückt/losgelassen an Position x/y.</summary>
    public static byte[] Button(byte button, bool down, ushort x, ushort y)
    {
        var b = new byte[7];
        b[0] = MouseButton;
        b[1] = button;
        b[2] = down ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(3), x);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(5), y);
        return b;
    }

    /// <summary>Mausrad; Delta wie unter Windows (120 je Raste), horizontal = Kipprad.</summary>
    public static byte[] Wheel(short delta, bool horizontal)
    {
        var b = new byte[4];
        b[0] = MouseWheel;
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(1), delta);
        b[3] = horizontal ? (byte)1 : (byte)0;
        return b;
    }

    /// <summary>Taste mit virtuellem Tastencode und Scancode; extended für Pfeil-, Einfg-, Entf-Tasten usw.</summary>
    public static byte[] KeyEvent(ushort vk, ushort scan, bool up, bool extended)
    {
        var b = new byte[6];
        b[0] = Key;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), vk);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(3), scan);
        b[5] = (byte)((up ? 1 : 0) | (extended ? 2 : 0));
        return b;
    }
}
