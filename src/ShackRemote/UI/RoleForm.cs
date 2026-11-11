namespace ShackRemote.UI;

/// <summary>Rollenauswahl beim Start: zwei große Karten.</summary>
public sealed class RoleForm : Form
{
    private readonly CheckBox _remember;

    public string? Role { get; private set; }
    public bool Remember => _remember.Checked;

    public RoleForm()
    {
        LayoutKit.PrepareForm(this, "ShackRemote – Rolle wählen");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(24, 18, 24, 18),
        };

        var question = new Label
        {
            Text = "Wie soll dieser PC verwendet werden?",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 13F),
            Margin = new Padding(0, 0, 0, 10),
        };
        var server = RoleCard("Server", "Dieser PC wird ferngesteuert – der Shack-PC.");
        server.Click += (_, _) => Choose("server");
        var client = RoleCard("Client", "Von hier aus einen anderen PC steuern – z. B. der Laptop.");
        client.Click += (_, _) => Choose("client");

        _remember = new CheckBox
        {
            Text = "Beim nächsten Start nicht mehr fragen (ändern mit Startparameter /rolle)",
            AutoSize = true,
            Margin = new Padding(2, 12, 0, 0),
        };

        root.Controls.Add(question);
        root.Controls.Add(server);
        root.Controls.Add(client);
        root.Controls.Add(_remember);
        // Fenster passt sich dem Inhalt an: Kopf und Inhalt untereinander in einer Tabelle
        var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        var header = LayoutKit.Header("ShackRemote", "Rolle dieses PCs wählen", LayoutKit.InfoButton(this));
        header.Dock = DockStyle.Fill;
        outer.Controls.Add(header, 0, 0);
        outer.Controls.Add(root, 0, 1);
        Controls.Add(outer);
        ResumeLayout(true);
    }

    /// <summary>Große, flache Karte: Titel fett, darunter die Erklärung.</summary>
    private Button RoleCard(string role, string description)
    {
        var b = new Button
        {
            Text = $"{role}\n{description}",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(460, 74),
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 6, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11F),
        };
        Theme.StyleButton(b, false);
        b.Tag = "themed";
        b.Padding = new Padding(16, 10, 16, 10);
        b.FlatAppearance.MouseOverBackColor = Theme.Hover;
        b.MouseEnter += (_, _) => b.FlatAppearance.BorderColor = Theme.Accent;
        b.MouseLeave += (_, _) => b.FlatAppearance.BorderColor = Theme.Border;
        return b;
    }

    private void Choose(string role)
    {
        Role = role;
        DialogResult = DialogResult.OK;
        Close();
    }
}
