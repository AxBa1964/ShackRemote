using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Hilfen für Fenster ohne feste Pixelpositionen: Alles liegt in Tabellen-/Fluss-Layouts,
/// Größen richten sich nach Inhalt und Schrift. So bleibt die Oberfläche bei jeder
/// Windows-Skalierung (100 %, 125 %, 150 % …) lesbar.
/// </summary>
internal static class LayoutKit
{
    private static Icon? AppIcon;   // Programmsymbol aus der exe (Titelleiste/Taskleiste)

    /// <summary>Muss im Konstruktor VOR dem Hinzufügen der Steuerelemente aufgerufen werden.</summary>
    public static void PrepareForm(Form form, string title)
    {
        form.SuspendLayout();
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        try { form.Icon = AppIcon ??= Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        form.Font = new Font("Segoe UI", 10F);
        form.Text = title;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = Theme.Surface;
        form.ForeColor = Theme.Text;
        Theme.DarkTitleBar(form);
        // Alle Steuerelemente im neuen Stil einfärben, sobald das Fenster aufgebaut ist
        form.Load += (_, _) => Theme.Apply(form);
    }

    /// <summary>
    /// Kopfzeile für Hauptfenster: Programmname groß, darunter Rolle/Rechner; rechts Platz für Zusatzelemente
    /// (z. B. Darstellung, Info). Mit Dock = Top NACH dem Hauptinhalt hinzufügen.
    /// </summary>
    public static Control Header(string title, string subtitle, params Control[] right)
    {
        var head = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Theme.Window,
            Padding = new Padding(16, 12, 12, 10),
            Margin = new Padding(0),
        };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        titles.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), Margin = new Padding(0) });
        titles.Controls.Add(new Label { Text = subtitle, AutoSize = true, ForeColor = Theme.SubtleText, Margin = new Padding(2, 0, 0, 0) });
        var side = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        side.Controls.AddRange(right);
        head.Controls.Add(titles, 0, 0);
        head.Controls.Add(side, 1, 0);

        // feine Trennlinie unten
        var wrap = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 1), BackColor = Theme.Border };
        wrap.Controls.Add(head);
        return wrap;
    }

    /// <summary>Auswahl der Darstellung (System/Hell/Dunkel); wirkt nach einem Neustart des Programms.</summary>
    public static Control ThemePicker(Form owner)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(6, 6, 3, 3) };
        box.Items.AddRange(new object[] { "System", "Hell", "Dunkel" });
        box.SelectedItem = Theme.Name(Theme.Parse(AppSettings.Read(s => s.Design)));
        box.SelectionChangeCommitted += (_, _) =>
        {
            var chosen = (string)box.SelectedItem!;
            AppSettings.Update(s => s.Design = chosen);
            if (ModernDialog.Show(owner, "Darstellung", $"Die Darstellung „{chosen}“ wird nach einem Neustart von ShackRemote übernommen. Jetzt neu starten?",
                    ("Jetzt neu starten", DialogResult.Yes, true), ("Später", DialogResult.No, false)) == DialogResult.Yes)
                Application.Restart();
        };
        new ToolTip().SetToolTip(box, "Darstellung: wie Windows, hell oder dunkel");
        var label = new Label { Text = "Darstellung", AutoSize = true, ForeColor = Theme.SubtleText, Margin = new Padding(6, 9, 0, 3) };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        row.Controls.Add(label);
        row.Controls.Add(box);
        return row;
    }

    /// <summary>Kleiner Knopf, der das Info-/Startfenster erneut zeigt.</summary>
    public static Button InfoButton(Form owner)
    {
        var b = MakeButton("Info");
        b.MinimumSize = new Size(70, 0);
        b.Margin = new Padding(6, 4, 3, 3);
        b.Click += (_, _) => { using var intro = new IntroForm(); intro.ShowDialog(owner); };
        return b;
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
