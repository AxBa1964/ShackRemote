using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>Server-Fenster (Shack-PC): Adressen, Status, Kopplungscode, Protokoll.</summary>
public sealed class ServerForm : Form
{
    private readonly string _fingerprint;
    private readonly SessionServer _server;
    private readonly DiscoveryResponder _discovery;

    private readonly CheckedListBox _addresses;
    private readonly Label _status;
    private readonly FlowLayoutPanel _pairingBox;
    private readonly Label _pairingFor;
    private readonly Label _pairingCode;
    private readonly TextBox _log;

    private volatile LocalAddress[] _active = Array.Empty<LocalAddress>();
    private bool _filling;

    public ServerForm(X509Certificate2 cert)
    {
        _fingerprint = Identity.Fingerprint(cert);

        LayoutKit.PrepareForm(this, $"ShackRemote Server {Protocol.Version} – {Environment.MachineName}");
        ClientSize = new Size(720, 600);
        MinimumSize = new Size(560, 480);

        // --- Adressen ---------------------------------------------------------------
        _addresses = new CheckedListBox
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            CheckOnClick = true,
            IntegralHeight = false,
            Height = 90,
        };
        // ItemCheck kommt VOR der Änderung – daher verzögert auswerten.
        _addresses.ItemCheck += (_, _) => { if (!_filling) BeginInvoke(new Action(ApplySelection)); };

        var refresh = LayoutKit.MakeButton("Neu einlesen");
        refresh.Click += (_, _) => FillAddresses();

        var addrRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        addrRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        addrRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        addrRow.Controls.Add(_addresses, 0, 0);
        addrRow.Controls.Add(LayoutKit.ButtonStack(refresh), 1, 0);

        // --- Status und Kopplung -------------------------------------------------------
        _status = LayoutKit.MakeLabel("Wartet auf Verbindung …", bold: true);

        _pairingFor = LayoutKit.MakeLabel("");
        _pairingFor.MaximumSize = new Size(640, 0);
        _pairingCode = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 28F, FontStyle.Bold),
            ForeColor = Color.DarkBlue,
            Margin = new Padding(3, 0, 3, 6),
        };
        _pairingBox = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Color.LightYellow,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8),
            Margin = new Padding(3, 6, 3, 6),
            Visible = false,
        };
        _pairingBox.Controls.Add(_pairingFor);
        _pairingBox.Controls.Add(_pairingCode);

        _log = LayoutKit.LogBox();

        // --- Gesamtlayout ------------------------------------------------------------------
        var root = LayoutKit.Column(7);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Überschrift Adressen
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Adressliste
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Zertifikat
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Ports
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Kopplung
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));     // Protokoll

        root.Controls.Add(LayoutKit.MakeLabel("Lauschen auf diesen IPv4-Adressen:"), 0, 0);
        root.Controls.Add(addrRow, 0, 1);
        root.Controls.Add(LayoutKit.MakeLabel($"Zertifikat dieses PCs: {Identity.Short(_fingerprint)}"), 0, 2);
        root.Controls.Add(LayoutKit.MakeLabel($"Ports: UDP {Protocol.DiscoveryPort} (Suche), TCP {Protocol.SessionPort} (Sitzung)"), 0, 3);
        root.Controls.Add(_status, 0, 4);
        root.Controls.Add(_pairingBox, 0, 5);
        root.Controls.Add(_log, 0, 6);
        Controls.Add(root);
        ResumeLayout(true);

        // --- Logik -------------------------------------------------------------------------
        _server = new SessionServer(cert);
        _server.Log += msg => Ui(() => Log(msg));
        ShackRemote.Core.Media.FfmpegLib.Log += msg => Ui(() => Log(msg));
        _server.PairingCodeShown += (code, name) => Ui(() =>
        {
            _pairingFor.Text = $"{name} möchte sich verbinden. Diesen Code dort eingeben (2 Minuten gültig):";
            _pairingCode.Text = $"{code[..3]} {code[3..]}";
            _pairingBox.Visible = true;
            WindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
            Activate();
        });
        _server.PairingEnded += () => Ui(() => _pairingBox.Visible = false);
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
        _addresses.Height = Math.Max(3, _addresses.Items.Count + 1) * _addresses.ItemHeight + 6;
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
            // Nur sichtbare Adressen ändern; gemerkte, momentan fehlende Adressen bleiben ausgeschlossen.
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
