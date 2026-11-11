using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ShackRemote.UI;

/// <summary>Gewählte Darstellung.</summary>
public enum AppTheme { System, Light, Dark }

/// <summary>
/// Gemeinsames Erscheinungsbild im Stil von Windows 11, hell oder dunkel: ruhige Flächen, flache Knöpfe mit
/// Symbolen aus der Windows-Symbolschrift ("Segoe Fluent Icons", unter Windows 10 "Segoe MDL2 Assets"),
/// Akzentfarbe für die Hauptaktion. Dunkler Modus über Application.SetColorMode (WinForms, experimentell,
/// erst ab Windows 11) plus eigene Farbpalette.
/// </summary>
internal static class Theme
{
    public static bool IsDark { get; private set; }

    // ---- Farbpalette (wird in Init gesetzt) ---------------------------------------------------
    public static Color Window { get; private set; } = Color.White;           // Listen, Eingabefelder, Karten
    public static Color Surface { get; private set; } = Color.FromArgb(243, 243, 243);   // Fensterhintergrund, Leisten
    public static Color Text { get; private set; } = Color.FromArgb(27, 27, 27);
    public static Color SubtleText { get; private set; } = Color.FromArgb(96, 96, 96);
    public static Color Border { get; private set; } = Color.FromArgb(225, 225, 225);
    public static Color Hover { get; private set; } = Color.FromArgb(230, 230, 230);
    public static Color Pressed { get; private set; } = Color.FromArgb(215, 215, 215);
    public static Color Accent { get; private set; } = Color.FromArgb(0, 103, 192);      // Windows-11-Standardblau
    public static Color AccentHover { get; private set; } = Color.FromArgb(25, 117, 197);
    public static Color AccentText { get; private set; } = Color.White;
    public static Color Highlight { get; private set; } = Color.FromArgb(255, 244, 206); // Hinweisflächen (Kopplungscode)

    // Symbole (Segoe Fluent Icons / Segoe MDL2 Assets; Codes laut Microsoft-Liste)
    public const char Up = '', Refresh = '', Copy = '', Move = '', NewFolder = '',
                      Rename = '', Delete = '', Cancel = '', Folder = '', Forward = '',
                      Back = '', Sync = '', Check = '', Document = '';

    public static string IconFontName { get; } =
        new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    /// <summary>Einmal beim Programmstart, vor dem ersten Fenster.</summary>
    public static void Init(AppTheme theme)
    {
#pragma warning disable WFO5001 // Dunkler Modus von WinForms ist (noch) als experimentell markiert
        Application.SetColorMode(theme switch
        {
            AppTheme.Dark => SystemColorMode.Dark,
            AppTheme.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System,
        });
        IsDark = Application.IsDarkModeEnabled;
#pragma warning restore WFO5001
        if (!IsDark) return;

        // Windows-11-Dunkelpalette
        Window = Color.FromArgb(43, 43, 43);
        Surface = Color.FromArgb(32, 32, 32);
        Text = Color.FromArgb(240, 240, 240);
        SubtleText = Color.FromArgb(170, 170, 170);
        Border = Color.FromArgb(60, 60, 60);
        Hover = Color.FromArgb(55, 55, 55);
        Pressed = Color.FromArgb(70, 70, 70);
        Accent = Color.FromArgb(76, 194, 255);
        AccentHover = Color.FromArgb(100, 205, 255);
        AccentText = Color.Black;
        Highlight = Color.FromArgb(60, 52, 20);
    }

    public static AppTheme Parse(string? s) => s switch
    {
        "Hell" => AppTheme.Light,
        "Dunkel" => AppTheme.Dark,
        _ => AppTheme.System,
    };

    public static string Name(AppTheme t) => t switch
    {
        AppTheme.Light => "Hell",
        AppTheme.Dark => "Dunkel",
        _ => "System",
    };

    // ---- Fenster und Steuerelemente einfärben -----------------------------------------------------

    /// <summary>Färbt ein Fenster samt allen Steuerelementen passend zur Palette ein (beim Laden aufgerufen).</summary>
    public static void Apply(Control root)
    {
        if (root is Form f)
        {
            f.BackColor = Surface;
            f.ForeColor = Text;
            DarkTitleBar(f);
        }
        foreach (Control c in root.Controls) ApplyOne(c);
    }

    private static void ApplyOne(Control c)
    {
        switch (c)
        {
            case Button b when !Equals(b.Tag, "themed"):
                StyleButton(b, Equals(b.Tag, "primary"));
                break;
            case TextBox t:
                t.BackColor = Window;
                t.ForeColor = Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ListView lv:
                lv.BackColor = Window;
                lv.ForeColor = Text;
                ExplorerStyle(lv);
                break;
            case ListBox lb:   // auch CheckedListBox
                lb.BackColor = Window;
                lb.ForeColor = Text;
                lb.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Window;
                cb.ForeColor = Text;
                break;
            case ProgressBar:
                break;
            default:
                if (IsDefaultText(c.ForeColor)) c.ForeColor = Text;
                else if (IsDark && c.ForeColor == Color.DarkBlue) c.ForeColor = Accent;
                if (c.BackColor == Color.LightYellow) c.BackColor = Highlight;
                break;
        }
        foreach (Control child in c.Controls) ApplyOne(child);
    }

    private static bool IsDefaultText(Color c) =>
        c.ToArgb() == SystemColors.ControlText.ToArgb() || c.ToArgb() == Color.Black.ToArgb() || c.ToArgb() == SystemColors.WindowText.ToArgb();

    /// <summary>Normaler Knopf im neuen Stil; primary = Akzentfarbe.</summary>
    public static void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.BackColor = primary ? Accent : Window;
        b.ForeColor = primary ? AccentText : Text;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? Accent : Pressed;
        b.Cursor = Cursors.Hand;
        if (b.Padding.Vertical < 8) b.Padding = new Padding(12, 5, 12, 5);
    }

    /// <summary>Knopf als Hauptaktion kennzeichnen (Akzentfarbe beim Einfärben).</summary>
    public static T Primary<T>(T b) where T : Button
    {
        b.Tag = "primary";
        return b;
    }

    /// <summary>Dunkle Titelleiste (Windows 10 ab 20H1 / Windows 11).</summary>
    public static void DarkTitleBar(Form f)
    {
        if (!IsDark) return;
        void Set()
        {
            int on = 1;
            DwmSetWindowAttribute(f.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        }
        if (f.IsHandleCreated) Set(); else f.HandleCreated += (_, _) => Set();
    }

    // ---- Bausteine ---------------------------------------------------------------------------------

    /// <summary>Symbol als Bild (für Knöpfe), passend zur Bildschirmskalierung des Steuerelements.</summary>
    public static Bitmap Glyph(char glyph, Control scaleFrom, Color color, int logicalSize = 16)
    {
        int size = scaleFrom.LogicalToDeviceUnits(logicalSize);
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(IconFontName, size * 0.75f, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, glyph.ToString(), font, new Rectangle(0, 0, size, size), color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        return bmp;
    }

    /// <summary>Flacher Knopf mit Symbol und Text (Werkzeugleiste).</summary>
    public static Button ToolButton(Control owner, char glyph, string text, string? tooltip = null, bool primary = false)
    {
        var b = new Button
        {
            Text = " " + text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? AccentText : Text,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(8, 4, 10, 4),
            Margin = new Padding(2, 4, 2, 4),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Tag = "themed",
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? Accent : Pressed;
        b.HandleCreated += (_, _) => b.Image = Glyph(glyph, b, primary ? AccentText : Text);
        if (tooltip is not null) new ToolTip().SetToolTip(b, tooltip);
        return b;
    }

    /// <summary>Quadratischer Symbolknopf (z. B. "eine Ebene höher").</summary>
    public static Button IconButton(char glyph, string tooltip)
    {
        var b = new Button
        {
            Size = new Size(34, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Window,
            Margin = new Padding(2, 3, 2, 3),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Tag = "themed",
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Hover;
        b.FlatAppearance.MouseDownBackColor = Pressed;
        b.HandleCreated += (_, _) => b.Image = Glyph(glyph, b, Text);
        new ToolTip().SetToolTip(b, tooltip);
        return b;
    }

    /// <summary>Dialogknopf; primary = Akzentfarbe (Standardaktion).</summary>
    public static Button DialogButton(string text, DialogResult result, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            DialogResult = result,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(110, 34),
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(10, 2, 10, 2),
            Tag = "themed",
        };
        StyleButton(b, primary);
        return b;
    }

    /// <summary>ListView im Stil des Windows-Explorers (Auswahl, Hover; dunkel: "DarkMode_Explorer").</summary>
    public static void ExplorerStyle(ListView list)
    {
        void Set() => SetWindowTheme(list.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
        if (list.IsHandleCreated) Set(); else list.HandleCreated += (_, _) => Set();
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>
/// Schlichter Dialog im Windows-11-Stil: Text, optional Eingabefeld, Knopfzeile rechts unten auf eigener Fläche.
/// Ersetzt MessageBox und den alten Eingabedialog (gleichmäßige Breite, Knöpfe immer in einer Zeile).
/// </summary>
internal sealed class ModernDialog : Form
{
    private readonly TextBox? _input;

    private ModernDialog(string title, string message, string? inputValue, (string Text, DialogResult Result, bool Primary)[] buttons)
    {
        LayoutKit.PrepareForm(this, title);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var body = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 16),
            BackColor = Theme.Window,
            Dock = DockStyle.Top,
        };
        var label = new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(460), 0),
            MinimumSize = new Size(LogicalToDeviceUnits(380), 0),
            Margin = new Padding(0, 0, 0, 12),
        };
        body.Controls.Add(label);
        if (inputValue is not null)
        {
            _input = new TextBox
            {
                Text = inputValue,
                Width = LogicalToDeviceUnits(460),
                Margin = new Padding(0, 0, 0, 4),
            };
            body.Controls.Add(_input);
        }

        var bar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(16, 12, 24, 12),
        };
        foreach (var (text, result, primary) in Enumerable.Reverse(buttons))
        {
            var b = Theme.DialogButton(text, result, primary);
            bar.Controls.Add(b);
            if (primary) AcceptButton = b;
            if (result == DialogResult.Cancel) CancelButton = b;
        }

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = new Padding(0) };
        root.Controls.Add(body, 0, 0);
        root.Controls.Add(bar, 0, 1);
        Controls.Add(root);
        ResumeLayout(true);

        Shown += (_, _) =>
        {
            bar.Width = body.Width;
            if (_input is not null)
            {
                _input.Focus();
                // Bei Dateinamen nur den Namen ohne Endung markieren (wie im Explorer)
                int dot = _input.Text.LastIndexOf('.');
                _input.Select(0, dot > 0 ? dot : _input.Text.Length);
            }
        };
    }

    public static string? Ask(IWin32Window owner, string title, string message, string initial = "")
    {
        using var d = new ModernDialog(title, message, initial,
            new[] { ("OK", DialogResult.OK, true), ("Abbrechen", DialogResult.Cancel, false) });
        return d.ShowDialog(owner) == DialogResult.OK ? d._input!.Text : null;
    }

    public static DialogResult Show(IWin32Window owner, string title, string message,
        params (string Text, DialogResult Result, bool Primary)[] buttons)
    {
        using var d = new ModernDialog(title, message, null, buttons);
        return d.ShowDialog(owner);
    }
}
