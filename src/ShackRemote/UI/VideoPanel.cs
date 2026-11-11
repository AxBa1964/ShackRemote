using ShackRemote.Core.Media;

namespace ShackRemote.UI;

/// <summary>
/// Anzeigefläche. Das Bild zeichnet der <see cref="VideoRenderer"/> per Swapchain direkt auf der GPU
/// in dieses Fenster; WinForms zeichnet hier nur den Hinweis, solange noch kein Bild da ist.
/// </summary>
public sealed class VideoPanel : Control
{
    public VideoRenderer? Renderer { get; set; }

    public VideoPanel()
    {
        // Kein Hintergrund löschen, kein Doppelpuffer: sonst flackert GDI über das GPU-Bild.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        BackColor = Color.Black;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Renderer?.HasPicture == true) return;
        e.Graphics.Clear(Color.Black);
        TextRenderer.DrawText(e.Graphics, "Warte auf Bild …", Font, ClientRectangle, Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
