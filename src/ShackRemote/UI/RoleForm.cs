namespace ShackRemote.UI;

/// <summary>Rollenauswahl beim Start.</summary>
public sealed class RoleForm : Form
{
    private readonly CheckBox _remember;

    public string? Role { get; private set; }
    public bool Remember => _remember.Checked;

    public RoleForm()
    {
        Text = "ShackRemote – Rolle wählen";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 230);

        var server = new Button
        {
            Text = "Server\r\nDieser PC wird ferngesteuert (Shack)",
            Location = new Point(20, 20),
            Size = new Size(420, 70),
        };
        server.Click += (_, _) => Choose("server");

        var client = new Button
        {
            Text = "Client\r\nIch steuere einen anderen PC (Laptop)",
            Location = new Point(20, 100),
            Size = new Size(420, 70),
        };
        client.Click += (_, _) => Choose("client");

        _remember = new CheckBox
        {
            Text = "Beim nächsten Start nicht mehr fragen (ändern mit Startparameter /rolle)",
            Location = new Point(20, 185),
            AutoSize = true,
        };

        Controls.AddRange(new Control[] { server, client, _remember });
    }

    private void Choose(string role)
    {
        Role = role;
        DialogResult = DialogResult.OK;
        Close();
    }
}
