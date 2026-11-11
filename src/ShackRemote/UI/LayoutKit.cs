namespace ShackRemote.UI;

/// <summary>
/// Hilfen für Fenster ohne feste Pixelpositionen: Alles liegt in Tabellen-/Fluss-Layouts,
/// Größen richten sich nach Inhalt und Schrift. So bleibt die Oberfläche bei jeder
/// Windows-Skalierung (100 %, 125 %, 150 % …) lesbar.
/// </summary>
internal static class LayoutKit
{
    /// <summary>Muss im Konstruktor VOR dem Hinzufügen der Steuerelemente aufgerufen werden.</summary>
    public static void PrepareForm(Form form, string title)
    {
        form.SuspendLayout();
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = new Font("Segoe UI", 10F);
        form.Text = title;
        form.StartPosition = FormStartPosition.CenterScreen;
    }

    /// <summary>Einspaltige Tabelle, die das ganze Fenster füllt.</summary>
    public static TableLayoutPanel Column(int rows)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            Padding = new Padding(10),
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        return t;
    }

    public static Label MakeLabel(string text, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 6, 3, 3),
        Font = bold ? new Font("Segoe UI", 10F, FontStyle.Bold) : null,
    };

    public static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(120, 0),
        Padding = new Padding(6, 3, 6, 3),
        Margin = new Padding(3, 3, 3, 6),
    };

    public static TextBox LogBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9.5F),
        BackColor = SystemColors.Window,
    };

    /// <summary>Senkrechte Knopfleiste (rechts neben Listen).</summary>
    public static FlowLayoutPanel ButtonStack(params Control[] buttons)
    {
        var f = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(6, 0, 0, 0),
        };
        f.Controls.AddRange(buttons);
        return f;
    }

    /// <summary>Waagerechte Zeile mit Elementen nebeneinander.</summary>
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        f.Controls.AddRange(controls);
        return f;
    }
}
