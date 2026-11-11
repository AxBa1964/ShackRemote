using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>Server-Fenster (Shack-PC): Adressen, Status, Kopplungscode, Protokoll.</summary>
public sealed class ServerForm : Form
{
    private readonly X509Certificate2 _cert;
    private readonly string _fingerprint;
    private readonly SessionServer _server;
    private readonly DiscoveryResponder _discovery;

    private readonly CheckedListBox _addresses;
    private readonly Label _status;
    private readonly Label _code;
    private readonly TextBox _log;

    private volatile LocalAddress[] _active = Array.Empty<LocalAddress>();
    private bool _filling;

    public ServerForm(X509Certificate2 cert)
    {
        _cert = cert;
        _fingerprint = Identity.Fingerprint(cert);

        Text = $"ShackRemote Server {Protocol.Version} – {Environment.MachineName}";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(500, 420);

        var addrLabel = new Label { Text = "Lauschen auf (IPv4):", Location = new Point(12, 12), AutoSize = true };
        _addresses = new CheckedListBox
        {
            Location = new Point(12, 32),
            Size = new Size(500, 90),
            CheckOnClick = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        // ItemCheck kommt VOR der Änderung – daher verzögert auswerten.
        _addresses.ItemCheck += (_, _) => { if (!_filling) BeginInvoke(new Action(ApplySelection)); };

        var refresh = new Button
        {
            Text = "Neu einlesen",
            Location = new Point(522, 32),
            Size = new Size(106, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        refresh.Click += (_, _) => FillAddresses();

        var fp = new Label
        {
            Text = $"Zertifikat: {Identity.Short(_fingerprint)}   Ports: UDP {Protocol.DiscoveryPort}, TCP {Protocol.SessionPort}",
            Location = new Point(12, 130),
            AutoSize = true,
        };

        _status = new Label
        {
            Text = "Wartet auf Verbindung …",
            Location = new Point(12, 155),
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
        };

        _code = new Label
        {
            Location = new Point(12, 180),
            Size = new Size(616, 60),
            Font = new Font(Font.FontFamily, 22f, FontStyle.Bold),
            ForeColor = Color.DarkBlue,
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _log = new TextBox
        {
            Location = new Point(12, 250),
            Size = new Size(616, 258),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        Controls.AddRange(new Control[] { addrLabel, _addresses, refresh, fp, _status, _code, _log });

        _server = new SessionServer(_cert);
        _server.Log += msg => Ui(() => Log(msg));
        _server.PairingCodeShown += (code, name) => Ui(() =>
        {
            _code.Text = $"Kopplungscode für {name}:  {code[..3]} {code[3..]}";
            _code.Visible = true;
            Activate();
        });
        _server.PairingEnded += () => Ui(() => _code.Visible = false);
        _server.ClientConnected += who => Ui(() => _status.Text = $"Verbunden: {who}");
        _server.ClientDisconnected += () => Ui(() => _status.Text = "Wartet auf Verbindung …");

        _discovery = new DiscoveryResponder(BuildInfo);
        _discovery.Log += msg => Ui(() => Log(msg));

        Load += (_, _) =>
        {
            FillAddresses();
            try { _discovery.Start(); }
            catch (Exception ex) { Log($"Server-Suche kann nicht starten: {ex.Message}"); }
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
        };

        FormClosed += (_, _) =>
        {
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            _discovery.Dispose();
            _server.Dispose();
        };
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Ui(() =>
    {
        Log("Netzwerkadressen haben sich geändert – neu einlesen");
        FillAddresses();
    });

    private void FillAddresses()
    {
        var excluded = AppSettings.Read(s => s.ExcludedAddresses.ToHashSet());
        _filling = true;
        _addresses.Items.Clear();
        foreach (var la in NetInfo.GetAll())
            _addresses.Items.Add(la, !excluded.Contains(la.Address.ToString()));
        _filling = false;
        ApplySelection();
    }

    private void ApplySelection()
    {
        if (_filling) return;

        var all = _addresses.Items.Cast<LocalAddress>().ToList();
        var chosen = _addresses.CheckedItems.Cast<LocalAddress>().ToArray();
        var notChosen = all.Except(chosen).Select(a => a.Address.ToString()).ToList();

        AppSettings.Update(s =>
        {
            // Nur Adressen ändern, die gerade sichtbar sind; ausgeschlossene, momentan fehlende bleiben gemerkt.
            s.ExcludedAddresses.RemoveAll(x => all.Any(a => a.Address.ToString() == x));
            s.ExcludedAddresses.AddRange(notChosen);
        });

        _active = chosen;
        _server.Start(chosen.Select(a => a.Address));
        if (chosen.Length == 0)
            Log("Keine Adresse ausgewählt – Server ist nicht erreichbar.");
    }

    private ServerInfo BuildInfo() => new()
    {
        Name = Environment.MachineName,
        Version = Protocol.Version,
        Fingerprint = _fingerprint,
        Port = Protocol.SessionPort,
        Addresses = _active.Select(a => new AdvertisedAddress { Address = a.Address.ToString(), Kind = a.Kind.ToString() }).ToList(),
    };

    private void Log(string message) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(action);
    }
}
