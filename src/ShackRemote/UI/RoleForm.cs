namespace ShackRemote.UI;

/// <summary>Rollenauswahl beim Start.</summary>
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

        var root = LayoutKit.Column(4);
        root.Dock = DockStyle.None;
        root.AutoSize = true;
        root.AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var title = LayoutKit.MakeLabel("Wie soll dieser PC verwendet werden?", bold: true);
        var server = RoleButton("Server", "Dieser PC wird ferngesteuert (Shack-PC)");
        server.Click += (_, _) => Choose("server");
        var client = RoleButton("Client", "Ich steuere von hier einen anderen PC (Laptop)");
        client.Click += (_, _) => Choose("client");

        _remember = new CheckBox
        {
            Text = "Beim nächsten Start nicht mehr fragen\n(wieder ändern: Startparameter /rolle)",
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 3),
        };

        root.Controls.Add(title);
        root.Controls.Add(server);
        root.Controls.Add(client);
        root.Controls.Add(_remember);
        Controls.Add(root);
        ResumeLayout(true);
    }

    private static Button RoleButton(string role, string description) => new()
    {
        Text = $"{role}\n{description}",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(420, 0),
        Padding = new Padding(10, 8, 10, 8),
        Margin = new Padding(3, 6, 3, 3),
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private void Choose(string role)
    {
        Role = role;
        DialogResult = DialogResult.OK;
        Close();
    }
}
