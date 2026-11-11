using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using ShackRemote.Core;
using ShackRemote.Core.Service;

namespace ShackRemote.UI;

/// <summary>Wie das Server-Fenster arbeitet.</summary>
public enum ServerMode
{
    /// <summary>Server läuft in diesem Fenster (ohne Dienst).</summary>
    Normal,
    /// <summary>Dienst läuft – dieses Fenster verwaltet nur (Dienst einrichten/entfernen).</summary>
    Management,
    /// <summary>Vom Dienst als SYSTEM gestarteter Server-Agent.</summary>
    Agent,
}

/// <summary>Server-Fenster (Shack-PC): Adressen, Status, Kopplungscode, Dienst, Protokoll.</summary>
public sealed class ServerForm : Form
{
    private ServerMode _mode;
    private readonly Label _serviceState;
    private readonly Button _serviceInstall;
    private readonly Button _serviceRemove;
    private readonly string _fingerprint;
    private readonly SessionServer _server;
    private DiscoveryResponder _discovery;

    private readonly CheckedListBox _addresses;
    private readonly Label _status;
    private readonly FlowLayoutPanel _pairingBox;
    private readonly Label _pairingFor;
    private readonly Label _pairingCode;
    private readonly TextBox _log;

    private volatile LocalAddress[] _active = Array.Empty<LocalAddress>();
    private bool _filling;

    public ServerForm(X509Certificate2 cert, ServerMode mode)
    {
        _mode = mode;
        _fingerprint = Identity.Fingerprint(cert);

        LayoutKit.PrepareForm(this, $"ShackRemote Server {Protocol.Version} – {Environment.MachineName}"
                                    + (mode == ServerMode.Agent ? " (Dienst)" : ""));
        if (mode == ServerMode.Agent) WindowState = FormWindowState.Minimized;
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

        // --- Dienst (für Sperrbildschirm, Strg+Alt+Entf, Start mit Windows) ------------------
        _serviceState = LayoutKit.MakeLabel("");
        _serviceState.MaximumSize = new Size(640, 0);
        _serviceInstall = LayoutKit.MakeButton("Dienst einrichten");
        _serviceRemove = LayoutKit.MakeButton("Dienst entfernen");
        _serviceInstall.Click += async (_, _) => await ServiceActionAsync(install: true);
        _serviceRemove.Click += async (_, _) => await ServiceActionAsync(install: false);
        var serviceRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        serviceRow.Controls.Add(_serviceInstall);
        serviceRow.Controls.Add(_serviceRemove);
        serviceRow.Controls.Add(_serviceState);
        _serviceState.Margin = new Padding(9, 9, 3, 3);
        if (mode == ServerMode.Agent) { _serviceInstall.Visible = false; _serviceRemove.Visible = false; }

        // --- Gesamtlayout ------------------------------------------------------------------
        var root = LayoutKit.Column(8);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Überschrift Adressen
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Adressliste
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Zertifikat
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Ports
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Dienst
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Kopplung
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));     // Protokoll

        root.Controls.Add(LayoutKit.MakeLabel("Lauschen auf diesen IPv4-Adressen:"), 0, 0);
        root.Controls.Add(addrRow, 0, 1);
        root.Controls.Add(LayoutKit.MakeLabel($"Zertifikat dieses PCs: {Identity.Short(_fingerprint)}"), 0, 2);
        root.Controls.Add(LayoutKit.MakeLabel($"Ports: UDP {Protocol.DiscoveryPort} (Suche), TCP {Protocol.SessionPort} (Sitzung)"), 0, 3);
        root.Controls.Add(serviceRow, 0, 4);
        root.Controls.Add(_status, 0, 5);
        root.Controls.Add(_pairingBox, 0, 6);
        root.Controls.Add(_log, 0, 7);
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
            if (_mode != ServerMode.Management) StartDiscovery();
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
            UpdateServiceState();
            if (_mode == ServerMode.Agent)
                Log("Läuft als Dienst (SYSTEM): auch Sperr-/Anmeldebildschirm und Strg+Alt+Entf möglich. Protokoll auch in " +
                    Path.Combine(ServiceSetup.DataFolder, "agent.log"));
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
        if (_mode == ServerMode.Management)
        {
            _server.Stop();
            return;   // der Dienst lauscht, nicht dieses Fenster
        }
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

    private void StartDiscovery()
    {
        try { _discovery.Start(); }
        catch (Exception ex) { Log($"Server-Suche kann nicht starten: {ex.Message}"); }
    }

    private void UpdateServiceState()
    {
        var status = ServiceSetup.Status();
        _serviceInstall.Text = status is null ? "Dienst einrichten" : "Dienst aktualisieren";
        _serviceRemove.Enabled = status is not null;
        _serviceState.Text = _mode switch
        {
            ServerMode.Agent => "Dienstbetrieb: Server startet mit Windows, Sperrbildschirm und Strg+Alt+Entf möglich.",
            ServerMode.Management => $"Dienst: {status}. Der Server läuft im Hintergrund – dieses Fenster verwaltet nur.",
            _ => status is null
                ? "Ohne Dienst: kein Sperr-/Anmeldebildschirm, kein Strg+Alt+Entf, kein Start mit Windows."
                : $"Dienst: {status}.",
        };
        _addresses.Enabled = _mode != ServerMode.Management;
        if (_mode == ServerMode.Management) _status.Text = "Server läuft als Dienst.";
    }

    /// <summary>Dienst einrichten/entfernen: eigenes Programm mit Adminrechten (UAC-Abfrage) aufrufen.</summary>
    private async Task ServiceActionAsync(bool install)
    {
        _serviceInstall.Enabled = _serviceRemove.Enabled = false;
        try
        {
            if (install && _mode == ServerMode.Normal)
            {
                // Ports freigeben – gleich übernimmt der Dienst
                _server.Stop();
                _discovery.Dispose();
                Log("Server in diesem Fenster angehalten – der Dienst übernimmt.");
            }
            Log(install ? "Richte Dienst ein (Windows fragt nach Adminrechten) …" : "Entferne Dienst (Windows fragt nach Adminrechten) …");
            int code = await Task.Run(() => ServiceSetup.RunElevated(install ? "/dienst-installieren" : "/dienst-entfernen"));
            Log(code == 0 ? "Fertig." : $"Abgebrochen oder fehlgeschlagen (Code {code}).");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log("Adminrechte wurden nicht erteilt.");
        }
        catch (Exception ex)
        {
            Log($"Fehler: {ex.Message}");
        }

        var newMode = ServiceSetup.IsRunning ? ServerMode.Management : ServerMode.Normal;
        if (newMode != _mode)
        {
            _mode = newMode;
            if (_mode == ServerMode.Normal)
            {
                _discovery = new DiscoveryResponder(BuildInfo);
                _discovery.Log += msg => Ui(() => Log(msg));
                StartDiscovery();
                _status.Text = "Wartet auf Verbindung …";
                Log("Server läuft wieder in diesem Fenster.");
            }
            ApplySelection();
        }
        else if (_mode == ServerMode.Normal && install)
        {
            // Einrichtung fehlgeschlagen: hier weiterlaufen
            _discovery = new DiscoveryResponder(BuildInfo);
            _discovery.Log += msg => Ui(() => Log(msg));
            StartDiscovery();
            ApplySelection();
        }
        UpdateServiceState();
        _serviceInstall.Enabled = true;
    }

    private void Log(string message)
    {
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        if (_mode == ServerMode.Agent) FileLog.Write("agent", message);
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(action);
    }
}
