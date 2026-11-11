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
        var custom = Path.Combine(AppContext.BaseDirectory, "intro.png");
        if (File.Exists(custom))
        {
            art = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = Image.FromFile(custom), Margin = new Padding(0) };
        }
        else
        {
            art = new IntroArt { Dock = DockStyle.Fill, Margin = new Padding(0) };
        }

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

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));
        split.Controls.Add(art, 0, 0);
        split.Controls.Add(text, 1, 0);
        Controls.Add(split);
        ResumeLayout(true);

        Shown += (_, _) => ok.Focus();
    }
}

/// <summary>
/// Gezeichnete Szene (flacher Illustrationsstil, skaliert mit der Fenstergröße):
/// links der Blick durchs Fenster auf einen Mann am PC, rechts auf einer Anhöhe das Shack mit Mast, Beam und
/// Drahtantenne; durch dessen Fenster sieht man denselben Bildschirminhalt – aber niemand sitzt davor.
/// Funkwellen deuten die Verbindung an. Beide Monitore zeigen denselben Wasserfall.
/// </summary>
internal sealed class IntroArt : Control
{
    public IntroArt()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float W = ClientSize.Width, H = ClientSize.Height;
        if (W < 10 || H < 10) return;
        PointF P(float x, float y) => new(x * W, y * H);
        RectangleF R(float x1, float y1, float x2, float y2) => RectangleF.FromLTRB(x1 * W, y1 * H, x2 * W, y2 * H);
        float L(float w) => Math.Max(1f, w * W);

        // --- Himmel (Abenddämmerung) -------------------------------------------------------
        using (var sky = new LinearGradientBrush(new RectangleF(0, 0, W, H), Color.Black, Color.Black, 90f))
        {
            sky.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(14, 22, 48), Color.FromArgb(44, 70, 120), Color.FromArgb(120, 110, 150), Color.FromArgb(240, 160, 95), Color.FromArgb(240, 160, 95) },
                Positions = new[] { 0f, 0.38f, 0.55f, 0.68f, 1f },
            };
            g.FillRectangle(sky, 0, 0, W, H);
        }
        var rnd = new Random(7);
        using (var star = new SolidBrush(Color.FromArgb(200, 255, 255, 240)))
            for (int i = 0; i < 45; i++)
            {
                float x = 0.45f + (float)rnd.NextDouble() * 0.55f, y = (float)rnd.NextDouble() * 0.36f, s = L(0.003f + (float)rnd.NextDouble() * 0.003f);
                g.FillEllipse(star, x * W, y * H, s, s);
            }
        using (var glow = new SolidBrush(Color.FromArgb(40, 255, 245, 210))) g.FillEllipse(glow, R(0.84f, 0.07f, 0.94f, 0.16f));
        using (var moon = new SolidBrush(Color.FromArgb(250, 242, 215))) g.FillEllipse(moon, R(0.865f, 0.09f, 0.915f, 0.135f));

        // --- Hügel ---------------------------------------------------------------------------
        using (var far = new GraphicsPath())
        {
            far.AddBezier(P(0.40f, 0.70f), P(0.55f, 0.62f), P(0.70f, 0.66f), P(1.0f, 0.63f));
            far.AddLine(P(1.0f, 0.63f), P(1.0f, 1.0f));
            far.AddLine(P(1.0f, 1.0f), P(0.40f, 1.0f));
            using var b = new SolidBrush(Color.FromArgb(46, 58, 92));
            g.FillPath(b, far);
        }
        using (var hill = new GraphicsPath())
        {
            hill.AddBezier(P(0.44f, 1.0f), P(0.58f, 0.70f), P(0.68f, 0.565f), P(0.78f, 0.565f));
            hill.AddBezier(P(0.78f, 0.565f), P(0.88f, 0.565f), P(0.95f, 0.60f), P(1.0f, 0.64f));
            hill.AddLine(P(1.0f, 0.64f), P(1.0f, 1.0f));
            hill.CloseFigure();
            using var b = new LinearGradientBrush(new RectangleF(0, 0.55f * H, W, 0.45f * H), Color.FromArgb(58, 108, 84), Color.FromArgb(24, 52, 44), 90f);
            g.FillPath(b, hill);
        }

        // --- Shack auf der Anhöhe ---------------------------------------------------------------
        using (var windowGlow = new SolidBrush(Color.FromArgb(50, 255, 210, 120))) g.FillEllipse(windowGlow, R(0.69f, 0.47f, 0.80f, 0.58f));
        using (var wall = new SolidBrush(Color.FromArgb(222, 205, 176))) g.FillRectangle(wall, R(0.70f, 0.49f, 0.86f, 0.567f));
        using (var roof = new SolidBrush(Color.FromArgb(140, 58, 52)))
            g.FillPolygon(roof, new[] { P(0.685f, 0.495f), P(0.78f, 0.425f), P(0.875f, 0.495f) });
        using (var lit = new SolidBrush(Color.FromArgb(255, 214, 122))) g.FillRectangle(lit, R(0.715f, 0.503f, 0.768f, 0.548f));
        // Monitor im Shack – derselbe Inhalt, leerer Platz davor
        DrawMonitor(g, R(0.724f, 0.509f, 0.759f, 0.532f), L(0.002f));
        using (var desk = new SolidBrush(Color.FromArgb(120, 82, 48))) g.FillRectangle(desk, R(0.715f, 0.538f, 0.768f, 0.543f));
        using (var frame = new Pen(Color.FromArgb(90, 70, 55), L(0.003f))) g.DrawRectangle(frame, Rect(R(0.715f, 0.503f, 0.768f, 0.548f)));
        using (var door = new SolidBrush(Color.FromArgb(96, 62, 44))) g.FillRectangle(door, R(0.80f, 0.515f, 0.83f, 0.567f));

        // Antennen: Mast mit Rotor und Yagi-Beam, dazu ein Vertikalstrahler
        using (var metal = new Pen(Color.FromArgb(205, 210, 220), L(0.004f)))
        {
            g.DrawLine(metal, P(0.845f, 0.47f), P(0.845f, 0.25f));                         // Mast
            g.DrawLine(metal, P(0.775f, 0.262f), P(0.915f, 0.262f));                       // Boom
            float[] xs = { 0.785f, 0.81f, 0.835f, 0.86f, 0.885f, 0.908f };
            float[] len = { 0.065f, 0.058f, 0.054f, 0.05f, 0.046f, 0.042f };
            using var element = new Pen(Color.FromArgb(225, 230, 240), L(0.0028f));
            for (int i = 0; i < xs.Length; i++)
                g.DrawLine(element, P(xs[i], 0.262f - len[i] / 2), P(xs[i], 0.262f + len[i] / 2));
            using var rotor = new SolidBrush(Color.FromArgb(90, 95, 105));
            g.FillRectangle(rotor, R(0.838f, 0.275f, 0.852f, 0.292f));
            g.DrawLine(metal, P(0.71f, 0.468f), P(0.71f, 0.37f));                          // Vertikal
            using var coax = new Pen(Color.FromArgb(40, 40, 40), L(0.002f));
            g.DrawBezier(coax, P(0.846f, 0.30f), P(0.86f, 0.38f), P(0.865f, 0.45f), P(0.858f, 0.50f));
        }

        // Funkwellen vom Beam in Richtung des Betrachters links
        using (var wave = new Pen(Color.FromArgb(150, 160, 215, 255), L(0.003f)))
            foreach (var r in new[] { 0.04f, 0.07f, 0.10f })
                g.DrawArc(wave, P(0.775f - r, 0.262f - r * W / H).X, P(0, 0.262f - r * W / H).Y, 2 * r * W, 2 * r * W, 150, 60);
        using (var link = new Pen(Color.FromArgb(110, 160, 215, 255), L(0.0025f)) { DashStyle = DashStyle.Dot })
            g.DrawBezier(link, P(0.66f, 0.262f), P(0.58f, 0.22f), P(0.50f, 0.25f), P(0.44f, 0.31f));

        // --- Hauswand links mit Blick ins Zimmer ---------------------------------------------------------
        using (var wallLeft = new SolidBrush(Color.FromArgb(54, 47, 43))) g.FillRectangle(wallLeft, R(0f, 0f, 0.47f, 1f));
        using (var brick = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
            for (float y = 0.02f; y < 1f; y += 0.035f) g.DrawLine(brick, P(0f, y), P(0.47f, y));
        using (var spill = new LinearGradientBrush(R(0.03f, 0.80f, 0.45f, 0.95f), Color.FromArgb(70, 255, 200, 130), Color.FromArgb(0, 255, 200, 130), 90f))
            g.FillRectangle(spill, R(0.03f, 0.80f, 0.45f, 0.95f));

        var room = R(0.05f, 0.30f, 0.42f, 0.80f);
        var state = g.Save();
        g.SetClip(room);
        using (var inside = new LinearGradientBrush(room, Color.FromArgb(122, 90, 64), Color.FromArgb(56, 40, 32), 90f)) g.FillRectangle(inside, room);
        using (var lampPath = new GraphicsPath())
        {
            lampPath.AddEllipse(R(-0.10f, 0.15f, 0.34f, 0.60f));
            using var lamp = new PathGradientBrush(lampPath) { CenterColor = Color.FromArgb(120, 255, 214, 160), SurroundColors = new[] { Color.FromArgb(0, 255, 214, 160) } };
            g.FillPath(lamp, lampPath);
        }
        // Schreibtisch, Monitor, Tastatur
        using (var desk = new SolidBrush(Color.FromArgb(124, 84, 50)))
        {
            g.FillRectangle(desk, R(0.06f, 0.665f, 0.42f, 0.688f));
            g.FillRectangle(desk, R(0.37f, 0.688f, 0.385f, 0.80f));
        }
        using (var stand = new SolidBrush(Color.FromArgb(30, 30, 34))) g.FillRectangle(stand, R(0.283f, 0.62f, 0.297f, 0.666f));
        using (var screenGlow = new SolidBrush(Color.FromArgb(40, 140, 190, 255))) g.FillEllipse(screenGlow, R(0.12f, 0.42f, 0.44f, 0.70f));
        DrawMonitor(g, R(0.19f, 0.47f, 0.39f, 0.625f), L(0.006f));
        using (var kb = new SolidBrush(Color.FromArgb(70, 70, 78))) g.FillRectangle(kb, R(0.215f, 0.652f, 0.33f, 0.665f));

        // Mann von schräg hinten: Stuhl, Oberkörper, Kopf mit Kopfhörer, Arm zur Tastatur
        using (var chair = new SolidBrush(Color.FromArgb(34, 34, 44)))
        using (var chairPath = RoundRect(R(0.065f, 0.565f, 0.165f, 0.76f), L(0.02f)))
            g.FillPath(chair, chairPath);
        using (var sweater = new SolidBrush(Color.FromArgb(62, 92, 140)))
        {
            using var torso = new GraphicsPath();
            torso.AddBezier(P(0.085f, 0.69f), P(0.08f, 0.60f), P(0.10f, 0.575f), P(0.145f, 0.572f));
            torso.AddBezier(P(0.145f, 0.572f), P(0.19f, 0.575f), P(0.21f, 0.60f), P(0.205f, 0.69f));
            torso.CloseFigure();
            g.FillPath(sweater, torso);
            using var arm = new Pen(Color.FromArgb(62, 92, 140), L(0.024f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(arm, P(0.19f, 0.60f), P(0.235f, 0.652f));
        }
        using (var skin = new SolidBrush(Color.FromArgb(214, 170, 136))) g.FillEllipse(skin, R(0.115f, 0.49f, 0.18f, 0.565f));
        using (var hair = new SolidBrush(Color.FromArgb(70, 70, 76)))
        {
            using var cap = new GraphicsPath();
            cap.AddArc(R(0.113f, 0.486f, 0.182f, 0.555f), 160, 220);
            cap.CloseFigure();
            g.FillPath(hair, cap);
        }
        using (var phones = new Pen(Color.FromArgb(25, 25, 28), L(0.007f)))
        {
            g.DrawArc(phones, Rect(R(0.11f, 0.48f, 0.185f, 0.56f)), 190, 160);
            using var cup = new SolidBrush(Color.FromArgb(25, 25, 28));
            g.FillEllipse(cup, R(0.163f, 0.515f, 0.19f, 0.548f));
        }
        g.Restore(state);

        // Fensterrahmen, Spiegelung, Fensterbank
        using (var glass = new LinearGradientBrush(room, Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 35f))
            g.FillPolygon(glass, new[] { P(0.05f, 0.30f), P(0.20f, 0.30f), P(0.05f, 0.55f) });
        using (var frame = new Pen(Color.FromArgb(232, 226, 214), L(0.014f))) g.DrawRectangle(frame, Rect(room));
        using (var sill = new SolidBrush(Color.FromArgb(205, 198, 186))) g.FillRectangle(sill, R(0.035f, 0.80f, 0.435f, 0.818f));
    }

    private static Rectangle Rect(RectangleF r) => Rectangle.Round(r);

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Monitor mit Rahmen und Bildinhalt (Spektrum und Wasserfall) – an beiden Orten identisch.</summary>
    private static void DrawMonitor(Graphics g, RectangleF bezel, float border)
    {
        using (var b = new SolidBrush(Color.FromArgb(18, 18, 22))) g.FillRectangle(b, bezel);
        var s = RectangleF.Inflate(bezel, -border, -border);
        if (s.Width < 2 || s.Height < 2) return;
        using (var bg = new SolidBrush(Color.FromArgb(8, 14, 32))) g.FillRectangle(bg, s);

        // Spektrum oben (feste Form, damit beide Monitore gleich aussehen)
        float specH = s.Height * 0.28f;
        float[] signals = { 0.22f, 0.55f, 0.80f };
        var pts = new List<PointF>();
        for (int i = 0; i <= 40; i++)
        {
            float x = i / 40f;
            float y = 0.15f + 0.05f * (float)Math.Sin(i * 1.7);
            foreach (var sx in signals) y += 0.7f * (float)Math.Exp(-Math.Pow((x - sx) / 0.025, 2));
            pts.Add(new PointF(s.Left + x * s.Width, s.Top + specH - Math.Min(1f, y) * specH * 0.9f));
        }
        using (var spec = new Pen(Color.FromArgb(120, 255, 140), Math.Max(1f, s.Width / 120f))) g.DrawLines(spec, pts.ToArray());

        // Wasserfall darunter
        float top = s.Top + specH, rows = 14, rowH = (s.Bottom - top) / rows;
        for (int r = 0; r < rows; r++)
        {
            int shade = 40 + (r * 37 % 30);
            using var row = new SolidBrush(Color.FromArgb(12, 24 + shade / 3, 70 + shade));
            g.FillRectangle(row, s.Left, top + r * rowH, s.Width, rowH + 0.5f);
            for (int k = 0; k < signals.Length; k++)
            {
                if ((r + k) % 5 == 4) continue;   // Pausen wie bei echten Aussendungen
                var c = k == 1 ? Color.FromArgb(255, 210, 70) : Color.FromArgb(255, 120, 60);
                using var sig = new SolidBrush(c);
                g.FillRectangle(sig, s.Left + (signals[k] - 0.015f) * s.Width, top + r * rowH, 0.03f * s.Width, rowH + 0.5f);
            }
        }
    }
}
