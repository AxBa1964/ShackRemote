namespace ShackRemote.UI;

/// <summary>Einfacher Eingabedialog (Kopplungscode, Ordnername …). Größe passt sich dem Text an.</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _input;

    private InputDialog(string title, string prompt)
    {
        LayoutKit.PrepareForm(this, title);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = LayoutKit.Column(3);
        root.Dock = DockStyle.None;
        root.AutoSize = true;
        root.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var label = new Label
        {
            Text = prompt,
            AutoSize = true,
            MaximumSize = new Size(440, 0),   // bricht lange Texte um
            Margin = new Padding(3, 3, 3, 10),
        };

        _input = new TextBox
        {
            Width = 440,
            Font = new Font("Segoe UI", 16F),
            Margin = new Padding(3, 3, 3, 10),
        };

        var ok = LayoutKit.MakeButton("OK");
        ok.DialogResult = DialogResult.OK;
        var cancel = LayoutKit.MakeButton("Abbrechen");
        cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        root.Controls.Add(label);
        root.Controls.Add(_input);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
        ResumeLayout(true);
    }

    /// <summary>
    /// Eingabe des 6-stelligen Kopplungscodes: nur Ziffern, Leerzeichen nach der 3. Ziffer kommt von selbst
    /// (wie im Server-Fenster "123 456"), OK erst bei 6 Ziffern. Eingefügter Text wird auf Ziffern bereinigt.
    /// Rückgabe: nur die 6 Ziffern, oder null bei Abbruch.
    /// </summary>
    public static string? AskCode(IWin32Window owner, string title, string prompt)
    {
        using var dlg = new InputDialog(title, prompt);
        var input = dlg._input;
        var ok = (Button)dlg.AcceptButton!;
        input.TextAlign = HorizontalAlignment.Center;
        input.Font = new Font("Segoe UI Semibold", 20F);
        input.PlaceholderText = "123 456";
        ok.Enabled = false;

        bool busy = false;
        input.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        input.TextChanged += (_, _) =>
        {
            if (busy) return;
            busy = true;
            var digits = new string(input.Text.Where(char.IsAsciiDigit).Take(6).ToArray());
            var formatted = digits.Length > 3 ? digits[..3] + " " + digits[3..] : digits;
            if (input.Text != formatted)
            {
                input.Text = formatted;
                input.SelectionStart = formatted.Length;
            }
            ok.Enabled = digits.Length == 6;
            busy = false;
        };

        return dlg.ShowDialog(owner) == DialogResult.OK
            ? new string(input.Text.Where(char.IsAsciiDigit).ToArray())
            : null;
    }

    /// <summary>Fragt einen Text ab (z. B. Ordnername); null bei Abbruch.</summary>
    public static string? Ask(IWin32Window owner, string title, string prompt, string initial = "")
    {
        using var dlg = new InputDialog(title, prompt);
        dlg._input.Text = initial;
        dlg._input.SelectAll();
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg._input.Text : null;
    }
}
