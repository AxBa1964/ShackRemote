namespace ShackRemote.UI;

/// <summary>Einfacher Eingabedialog (Kopplungscode, manuelle Adresse).</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _input;

    private InputDialog(string title, string prompt)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(420, 150);

        var label = new Label { Text = prompt, Location = new Point(15, 15), Size = new Size(390, 50) };
        _input = new TextBox { Location = new Point(15, 70), Width = 390, Font = new Font(Font.FontFamily, 14f) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(230, 112), Width = 85 };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(320, 112), Width = 85 };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { label, _input, ok, cancel });
    }

    public static string? Ask(IWin32Window owner, string title, string prompt)
    {
        using var dlg = new InputDialog(title, prompt);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg._input.Text : null;
    }
}
