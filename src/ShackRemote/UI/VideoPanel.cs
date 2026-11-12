using System.ComponentModel;
using ShackRemote.Core.Media;

namespace ShackRemote.UI;

/// <summary>
/// Anzeigefläche. Das Bild zeichnet der <see cref="VideoRenderer"/> per Swapchain direkt auf der GPU
/// in dieses Fenster; WinForms zeichnet hier nur den Hinweis, solange noch kein Bild da ist.
/// </summary>
public sealed class VideoPanel : Control
{
    /// <summary>Renderer, der in dieses Panel zeichnet (für Größenänderungen).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VideoRenderer? Renderer { get; set; }

    /// <summary>Waagrechtes Mausrad (WM_MOUSEHWHEEL), das WinForms nicht als Ereignis anbietet.</summary>
    public event Action<short>? HorizontalWheel;

    public VideoPanel()
    {
        SetStyle(ControlStyles.Selectable, true);   // braucht den Fokus für das Mausrad
        TabStop = true;
        // Kein Hintergrund löschen, kein Doppelpuffer: sonst flackert GDI über das GPU-Bild.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        BackColor = Color.Black;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (m.Msg == WM_MOUSEHWHEEL)
        {
            HorizontalWheel?.Invoke((short)((long)m.WParam >> 16 & 0xFFFF));
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    // Pfeil- und Tabulatortasten nicht für die Fokus-Navigation verbrauchen
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Renderer?.HasPicture == true) return;
        e.Graphics.Clear(Color.Black);
        TextRenderer.DrawText(e.Graphics, "Warte auf Bild …", Font, ClientRectangle, Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
