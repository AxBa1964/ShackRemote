using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ShackRemote.UI;

/// <summary>
/// Gemeinsames Erscheinungsbild im Stil von Windows 11: helle Flächen, flache Knöpfe mit Symbolen aus der
/// Windows-Symbolschrift ("Segoe Fluent Icons", unter Windows 10 "Segoe MDL2 Assets"), Akzentfarbe für die Hauptaktion.
/// Symbol-Codes laut Microsoft-Liste (gleich in beiden Schriften).
/// </summary>
internal static class Theme
{
    public static readonly Color Window = Color.White;
    public static readonly Color Surface = Color.FromArgb(243, 243, 243);      // Leisten, Fensterhintergrund
    public static readonly Color Border = Color.FromArgb(225, 225, 225);
    public static readonly Color Hover = Color.FromArgb(230, 230, 230);
    public static readonly Color Pressed = Color.FromArgb(215, 215, 215);
    public static readonly Color Accent = Color.FromArgb(0, 103, 192);         // Windows-11-Standardblau
    public static readonly Color AccentHover = Color.FromArgb(25, 117, 197);
    public static readonly Color SubtleText = Color.FromArgb(96, 96, 96);

    // Symbole (Segoe Fluent Icons / Segoe MDL2 Assets)
    public const char Up = '', Refresh = '', Copy = '', Move = '', NewFolder = '',
                      Rename = '', Delete = '', Cancel = '', Folder = '', Forward = '',
                      Back = '', Sync = '', Check = '', Document = '';

    public static string IconFontName { get; } =
        new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

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
            ForeColor = primary ? Color.White : Color.Black,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(8, 4, 10, 4),
            Margin = new Padding(2, 4, 2, 4),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? Accent : Pressed;
        b.HandleCreated += (_, _) => b.Image = Glyph(glyph, b, primary ? Color.White : Color.FromArgb(32, 32, 32));
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
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Hover;
        b.FlatAppearance.MouseDownBackColor = Pressed;
        b.HandleCreated += (_, _) => b.Image = Glyph(glyph, b, Color.FromArgb(32, 32, 32));
        new ToolTip().SetToolTip(b, tooltip);
        return b;
    }

    /// <summary>Normaler Dialogknopf; primary = Akzentfarbe (Standardaktion).</summary>
    public static Button DialogButton(string text, DialogResult result, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            DialogResult = result,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Window,
            ForeColor = primary ? Color.White : Color.Black,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(10, 2, 10, 2),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(200, 200, 200);
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
        return b;
    }

    /// <summary>ListView im Stil des Windows-Explorers (Auswahl, Hover).</summary>
    public static void ExplorerStyle(ListView list)
    {
        list.HandleCreated += (_, _) => SetWindowTheme(list.Handle, "Explorer", null);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}

/// <summary>
/// Schlichter Dialog im Windows-11-Stil: Text, optional Eingabefeld, Knopfzeile rechts unten auf grauer Fläche.
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
        BackColor = Theme.Window;
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
