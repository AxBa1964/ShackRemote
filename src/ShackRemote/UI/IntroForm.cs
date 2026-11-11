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
        ClientSize = new Size(980, 600);

        // --- Bild ----------------------------------------------------------------------------
        Control art;
        var image = LoadIntroImage();
        if (image is not null)
            art = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = image, Margin = new Padding(0), BackColor = Theme.Surface };
        else
            art = new IntroArt { Dock = DockStyle.Fill, Margin = new Padding(0) };

        // --- Text ----------------------------------------------------------------------------
        var title = new Label { Text = "ShackRemote", AutoSize = true, Font = new Font("Segoe UI Semibold", 28F), Margin = new Padding(0, 0, 0, 0) };
        var tagline = new Label
        {
            Text = "Fernsteuerung für die Funkstation – Bild, Ton, Maus, Tastatur und Dateien",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = Theme.SubtleText,
            Margin = new Padding(2, 0, 0, 14),
        };
        var version = new Label { Text = $"Version {Protocol.Version}", AutoSize = true, Margin = new Padding(2, 0, 0, 2) };
        var author = new Label
        {
            Text = $"© {DateTime.Now.Year} Axel Baruschke · DK7EE",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F),
            Margin = new Padding(2, 0, 0, 2),
        };
        var free = new Label
        {
            Text = "Software zur freien Verwendung – kostenlos nutzen und weitergeben.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(2, 0, 0, 12),
        };
        var license = new TextBox
        {
            Text = LicenseText,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            TabStop = false,
        };

        var ok = Theme.Primary(LayoutKit.MakeButton("OK"));
        ok.MinimumSize = new Size(120, 0);
        ok.DialogResult = DialogResult.OK;
        AcceptButton = ok;
        CancelButton = ok;

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(LayoutKit.ThemePicker(this), 0, 0);
        bottom.Controls.Add(ok, 1, 0);

        var text = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(28, 26, 28, 18), BackColor = Theme.Window };
        for (int i = 0; i < 6; i++) text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        text.Controls.Add(title, 0, 0);
        text.Controls.Add(tagline, 0, 1);
        text.Controls.Add(version, 0, 2);
        text.Controls.Add(author, 0, 3);
        text.Controls.Add(free, 0, 4);
        text.Controls.Add(new Label { Text = "Lizenz und Haftung", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(2, 0, 0, 4) }, 0, 5);
        text.Controls.Add(license, 0, 6);
        text.Controls.Add(bottom, 0, 7);

        // Bildspalte nach Seitenverhältnis des Bildes (Querformat breiter), Textspalte fest daneben
        int artWidth = 440;
        if (image is not null && image.Height > 0)
            artWidth = (int)Math.Clamp(600.0 * image.Width / image.Height, 360, 700);
        ClientSize = new Size(artWidth + 520, 600);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        // Prozentangaben statt fester Pixel: skaliert sicher mit der Windows-Anzeigeskalierung
        float artShare = 100f * artWidth / (artWidth + 520);
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, artShare));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f - artShare));
        split.Controls.Add(art, 0, 0);
        split.Controls.Add(text, 1, 0);
        Controls.Add(split);
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
