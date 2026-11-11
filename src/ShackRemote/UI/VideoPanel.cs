using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ShackRemote.UI;

/// <summary>
/// Zeigt das zuletzt dekodierte Bild mittig an (bereits auf Anzeigegröße skaliert, daher 1:1 gezeichnet).
/// Bilder kommen aus dem Decoder-Thread; gezeichnet wird im UI-Thread.
/// </summary>
public sealed class VideoPanel : Control
{
    private readonly object _gate = new();
    private Bitmap? _bitmap;
    private int _invalidatePending;

    public int FramesShown { get; private set; }

    /// <summary>Rechteck, in dem das Bild gerade liegt (für Mauskoordinaten in Stufe 1c).</summary>
    public Rectangle ImageBounds { get; private set; }

    public VideoPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        BackColor = Color.Black;
    }

    /// <summary>Aus dem Decoder-Thread: BGRA-Puffer in die Bitmap kopieren und Neuzeichnen anstoßen.</summary>
    public void UpdateFrame(byte[] bgra, int width, int height)
    {
        lock (_gate)
        {
            if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            }
            var data = _bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                if (data.Stride == width * 4)
                {
                    Marshal.Copy(bgra, 0, data.Scan0, width * height * 4);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(bgra, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
                }
            }
            finally
            {
                _bitmap.UnlockBits(data);
            }
        }

        // Nur ein Neuzeichnen gleichzeitig anstoßen – sonst staut sich die Nachrichtenschleife.
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 0 && IsHandleCreated && !IsDisposed)
        {
            try { BeginInvoke(new Action(() => { Invalidate(); })); }
            catch (InvalidOperationException) { /* Fenster wird geschlossen */ }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Interlocked.Exchange(ref _invalidatePending, 0);
        var g = e.Graphics;
        lock (_gate)
        {
            if (_bitmap is null)
            {
                g.Clear(Color.Black);
                TextRenderer.DrawText(g, "Warte auf Bild …", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int x = (ClientSize.Width - _bitmap.Width) / 2;
            int y = (ClientSize.Height - _bitmap.Height) / 2;
            ImageBounds = new Rectangle(x, y, _bitmap.Width, _bitmap.Height);

            using var black = new SolidBrush(Color.Black);
            // Ränder schwarz füllen (Bild liegt mittig, Seitenverhältnis bleibt erhalten)
            if (y > 0) { g.FillRectangle(black, 0, 0, ClientSize.Width, y); g.FillRectangle(black, 0, y + _bitmap.Height, ClientSize.Width, ClientSize.Height); }
            if (x > 0) { g.FillRectangle(black, 0, 0, x, ClientSize.Height); g.FillRectangle(black, x + _bitmap.Width, 0, ClientSize.Width, ClientSize.Height); }

            g.DrawImageUnscaled(_bitmap, x, y);
            FramesShown++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
        }
        base.Dispose(disposing);
    }
}
