using System.Drawing.Drawing2D;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Startfenster: links das Bild, rechts Name, Version, Urheber, Hinweis zur freien Verwendung und Haftungsausschluss.
/// Liegt neben der Exe eine Datei "intro.png", wird sie statt der gezeichneten Szene gezeigt.
/// Erscheint nicht im Dienstbetrieb.
/// </summary>
public sealed class IntroForm : Form
{
    private const string LicenseText =
        "Haftungsausschluss\r\n" +
        "Diese Software wird kostenlos und ohne jegliche Gewährleistung bereitgestellt – weder ausdrücklich noch " +
        "stillschweigend, insbesondere nicht für Fehlerfreiheit, ständige Verfügbarkeit oder die Eignung für einen " +
        "bestimmten Zweck. Die Nutzung erfolgt auf eigene Gefahr.\r\n\r\n" +
        "Eine Haftung für Schäden, die aus der Nutzung oder der Nichtnutzbarkeit entstehen – auch an Hard- und Software, " +
        "Daten oder Funkanlagen –, ist ausgeschlossen, soweit gesetzlich zulässig. Unberührt bleibt die Haftung bei " +
        "Vorsatz und grober Fahrlässigkeit sowie für Schäden aus der Verletzung von Leben, Körper oder Gesundheit.\r\n\r\n" +
        "Fernzugriff nur auf eigene Rechner oder mit Erlaubnis des Besitzers.\r\n\r\n" +
        "Verwendete Bausteine\r\n" +
        "FFmpeg (ffmpeg.org) – in der beiliegenden Fassung unter GNU GPL v3; FFmpeg.AutoGen (MIT-Lizenz); " +
        "NAudio (MIT-Lizenz). Für diese Bausteine gelten ihre jeweiligen Lizenzen.";

    public IntroForm()
    {
        LayoutKit.PrepareForm(this, "ShackRemote");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // --- Bild oben (volle Breite) ----------------------------------------------------------
        Control art;
        var image = LoadIntroImage();
        if (image is not null)
            art = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, Margin = new Padding(0), BackColor = Theme.Surface };
        else
            art = new IntroArt { Dock = DockStyle.Fill, Margin = new Padding(0) };

        // Größe: Breite 860, Bildhöhe nach Seitenverhältnis – aber so, dass das Fenster auf den Bildschirm passt
        const int textHeight = 390;
        int width = 860;
        int imageHeight = image is not null && image.Width > 0 ? width * image.Height / image.Width : 260;
        float scale = DeviceDpi / 96f;
        int available = (int)(Screen.FromPoint(Cursor.Position).WorkingArea.Height / scale * 0.92f) - textHeight - 40;
        if (imageHeight > available && available > 150)
        {
            if (image is not null) width = Math.Max(640, available * image.Width / image.Height);
            imageHeight = available;
        }
        ClientSize = new Size(width, imageHeight + textHeight);

        // --- Text darunter, zentriert --------------------------------------------------------
        Label Centered(string text, Font? font = null, Color? color = null, int bottom = 2) => new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Top,
            TextAlign = ContentAlignment.MiddleCenter,
            MaximumSize = new Size(width - 60, 0),
            Font = font,
            ForeColor = color ?? Theme.Text,
            Margin = new Padding(0, 0, 0, bottom),
        };

        var license = new TextBox
        {
            Text = LicenseText,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top,
            Width = width - 120,
            Height = 112,
            Font = new Font("Segoe UI", 9F),
            TabStop = false,
            Margin = new Padding(0, 4, 0, 10),
        };

        var ok = Theme.Primary(LayoutKit.MakeButton("OK"));
        ok.MinimumSize = new Size(140, 0);
        ok.DialogResult = DialogResult.OK;
        AcceptButton = ok;
        CancelButton = ok;

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top, Margin = new Padding(0) };
        buttons.Controls.Add(LayoutKit.ThemePicker(this));
        buttons.Controls.Add(new Label { Width = 24 });   // Abstand
        buttons.Controls.Add(ok);

        var text = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(24, 14, 24, 14),
            BackColor = Theme.Window,
        };
        text.Controls.Add(Centered("ShackRemote", new Font("Segoe UI Semibold", 22F)));
        text.Controls.Add(Centered("Fernsteuerung für die Funkstation – Bild, Ton, Maus, Tastatur und Dateien", color: Theme.SubtleText, bottom: 8));
        text.Controls.Add(Centered($"Version {Protocol.Version}   ·   © {DateTime.Now.Year} Axel Baruschke · DK7EE", new Font("Segoe UI Semibold", 10.5F)));
        text.Controls.Add(Centered("Software zur freien Verwendung – kostenlos nutzen und weitergeben.", bottom: 6));
        text.Controls.Add(license);
        text.Controls.Add(buttons);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(art, 0, 0);
        root.Controls.Add(text, 0, 1);
        Controls.Add(root);
        ResumeLayout(true);

        Shown += (_, _) => ok.Focus();
    }

    /// <summary>
    /// Bild für die linke Seite: zuerst "intro.png" neben der Exe (zum Ausprobieren), sonst das in die Exe
    /// eingebettete Assets\intro.png. Ohne Bild: ruhige Fläche (IntroArt).
    /// </summary>
    private static Image? LoadIntroImage()
    {
        try
        {
            var loose = Path.Combine(AppContext.BaseDirectory, "intro.png");
            if (File.Exists(loose)) return Image.FromFile(loose);
            using var stream = typeof(IntroForm).Assembly.GetManifestResourceStream("ShackRemote.Assets.intro.png");
            return stream is null ? null : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Platzhalter, solange kein Bild vorhanden ist: Farbverlauf in Akzentfarbe, dezente Funkwellen-Ringe
/// und ein großes Symbol "Remote" aus der Windows-Symbolschrift.
/// </summary>
internal sealed class IntroArt : Control
{
    private const char RemoteGlyph = '\uE8AF';   // "Remote" laut Microsoft-Symbolliste

    public IntroArt()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        if (r.Width < 10 || r.Height < 10) return;

        var top = Theme.IsDark ? Color.FromArgb(10, 40, 78) : Color.FromArgb(0, 84, 166);
        var bottom = Theme.IsDark ? Color.FromArgb(4, 12, 28) : Color.FromArgb(0, 40, 90);
        using (var bg = new LinearGradientBrush(r, top, bottom, 60f)) g.FillRectangle(bg, r);

        // Funkwellen-Ringe um die Bildmitte
        var c = new PointF(r.Width * 0.5f, r.Height * 0.45f);
        for (int i = 1; i <= 6; i++)
        {
            float rad = Math.Min(r.Width, r.Height) * 0.11f * i;
            using var ring = new Pen(Color.FromArgb(Math.Max(10, 70 - i * 10), 255, 255, 255), Math.Max(1f, r.Width / 400f));
            g.DrawEllipse(ring, c.X - rad, c.Y - rad, 2 * rad, 2 * rad);
        }

        // Großes Symbol
        float size = Math.Min(r.Width, r.Height) * 0.28f;
        using (var font = new Font(Theme.IconFontName, size, GraphicsUnit.Pixel))
            TextRenderer.DrawText(g, RemoteGlyph.ToString(), font,
                new Rectangle((int)(c.X - size), (int)(c.Y - size), (int)(2 * size), (int)(2 * size)),
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Schriftzug unten
        using var caption = new Font("Segoe UI Semibold", Math.Max(10f, r.Width / 26f), GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "DK7EE · Shack", caption, new Rectangle(0, (int)(r.Height * 0.80f), r.Width, (int)(r.Height * 0.1f)),
            Color.FromArgb(200, 255, 255, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
