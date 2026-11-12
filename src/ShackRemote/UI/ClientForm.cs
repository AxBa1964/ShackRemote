using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>Client-Fenster (Laptop): Server suchen (Heimnetz, bekannte Adressen, Tailscale), auswählen, verbinden und koppeln; öffnet danach das Fernsteuerungsfenster.</summary>
public sealed class ClientForm : Form
{
    private readonly X509Certificate2 _cert;
    private readonly ListView _servers;
    private readonly Button _search;
    private readonly Button _connect;
    private readonly Button _disconnect;
    private readonly Button _manualConnect;
    private readonly TextBox _manual;
    private readonly Label _status;
    private readonly Label _rtt;
    private readonly TextBox _log;

    private SessionClient? _session;
    private RemoteForm? _remote;

    public ClientForm(X509Certificate2 cert)
    {
        _cert = cert;

        LayoutKit.PrepareForm(this, $"ShackRemote Client {Protocol.Version} – {Environment.MachineName}");
        ClientSize = new Size(760, 600);
        MinimumSize = new Size(600, 480);

        // --- Serverliste mit Knöpfen ---------------------------------------------------
        _servers = new ListView
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Height = 160,
        };
        _servers.Columns.Add("Server");
        _servers.Columns.Add("Adresse");
        _servers.Columns.Add("Netz");
        _servers.Columns.Add("Status");
        _servers.DoubleClick += async (_, _) => await ConnectSelectedAsync();

        _search = LayoutKit.MakeButton("Suchen");
        _search.Click += async (_, _) => await SearchAsync();
        _connect = Theme.Primary(LayoutKit.MakeButton("Verbinden"));
        _connect.Click += async (_, _) => await ConnectSelectedAsync();
        _disconnect = LayoutKit.MakeButton("Trennen");
        _disconnect.Enabled = false;
        _disconnect.Click += async (_, _) => await DisconnectAsync("Getrennt.");

        var listRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0) };
        listRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        listRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listRow.Controls.Add(_servers, 0, 0);
        listRow.Controls.Add(LayoutKit.ButtonStack(_search, _connect, _disconnect), 1, 0);

        // --- Manuelle Eingabe ------------------------------------------------------------
        var manualLabel = LayoutKit.MakeLabel("Oder Adresse eingeben (IPv4):");
        _manual = new TextBox { Width = 180, Margin = new Padding(3, 4, 3, 3) };
        _manualConnect = LayoutKit.MakeButton("Verbinden");
        _manualConnect.Click += async (_, _) => await ConnectManualAsync();
        var manualRow = LayoutKit.Row(manualLabel, _manual, _manualConnect);

        // --- Status und Protokoll --------------------------------------------------------
        _status = LayoutKit.MakeLabel("Nicht verbunden", bold: true);
        _rtt = LayoutKit.MakeLabel("");
        _log = LayoutKit.LogBox();

        var root = LayoutKit.Column(6);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Überschrift
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Liste
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Manuell
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // Umlaufzeit
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));     // Protokoll

        root.Controls.Add(LayoutKit.MakeLabel("Gefundene Server (Doppelklick verbindet):"), 0, 0);
        root.Controls.Add(listRow, 0, 1);
        root.Controls.Add(manualRow, 0, 2);
        root.Controls.Add(_status, 0, 3);
        root.Controls.Add(_rtt, 0, 4);
        root.Controls.Add(_log, 0, 5);
        Controls.Add(root);
        Controls.Add(LayoutKit.Header("ShackRemote Client", $"{Environment.MachineName} · Version {Protocol.Version}",
            LayoutKit.ThemePicker(this), LayoutKit.InfoButton(this)));
        ResumeLayout(true);

        Log($"Eigenes Zertifikat: {Identity.Short(Identity.Fingerprint(_cert))}");
        Shown += async (_, _) => { ResizeColumns(); await SearchAsync(); };
        FormClosing += async (_, _) => { if (_session is not null) await _session.DisposeAsync(); };
    }

    private void ResizeColumns()
    {
        // Jede Spalte so breit wie Inhalt ODER Überschrift – je nachdem, was breiter ist.
        foreach (ColumnHeader c in _servers.Columns)
        {
            c.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            int content = c.Width;
            c.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
            c.Width = Math.Max(content, c.Width);
        }
    }

    private async Task SearchAsync()
    {
        _search.Enabled = false;
        _servers.Items.Clear();
        Log("Suche Server …");
        try
        {
            var known = AppSettings.Read(s => s.KnownServers.ToList());
            var own = NetInfo.GetAll().Select(a => a.Address).ToHashSet();
            var targets = known.SelectMany(k => k.Addresses)
                               .Select(a => IPAddress.TryParse(a, out var ip) ? ip : null)
                               .OfType<IPAddress>()
                               .ToList();

            // Über Tailscale gibt es keinen Broadcast: alle Geräte, die dort gerade online sind, gezielt fragen
            var peers = await TailscalePeers.OnlineAsync(msg => Log(msg));
            if (peers.Count > 0)
            {
                Log($"Tailscale: {peers.Count} Gerät(e) online ({string.Join(", ", peers.Select(p => p.Name).Distinct())})");
                targets.AddRange(peers.Select(p => p.Address));
            }
            targets = targets.Where(ip => !own.Contains(ip)).Distinct().ToList();

            var found = await DiscoveryClient.SearchAsync(targets, TimeSpan.FromSeconds(1.5), msg => Log(msg));
            foreach (var f in found.OrderBy(f => f.Info.Name))
            {
                var isKnown = known.Any(k => string.Equals(k.Fingerprint, f.Info.Fingerprint, StringComparison.OrdinalIgnoreCase));
                var kind = f.Info.Addresses.FirstOrDefault(a => a.Address == f.RespondedFrom.ToString())?.Kind ?? "?";
                _servers.Items.Add(new ListViewItem(new[]
                {
                    f.Info.Name,
                    f.RespondedFrom.ToString(),
                    kind,
                    isKnown ? "gekoppelt" : "neu – Kopplung nötig",
                }) { Tag = f });
            }
            ResizeColumns();
            Log(found.Count == 0 ? "Kein Server gefunden." : $"{found.Count} Server gefunden.");
            if (_servers.Items.Count > 0) _servers.Items[0].Selected = true;
        }
        catch (Exception ex)
        {
            Log($"Suche fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            _search.Enabled = true;
        }
    }

    private async Task ConnectSelectedAsync()
    {
        if (_servers.SelectedItems.Count == 0 || _servers.SelectedItems[0].Tag is not FoundServer f)
        {
            Log("Bitte einen Server in der Liste auswählen.");
            return;
        }
        // Erwarteter Fingerabdruck: was beim letzten Mal unter diesem Namen gespeichert wurde.
        var expected = AppSettings.Read(s => s.KnownServers.FirstOrDefault(k => k.Name == f.Info.Name)?.Fingerprint);
        await ConnectAsync(f.RespondedFrom, expected, f.Info.Addresses.Select(a => a.Address).ToList());
    }

    private async Task ConnectManualAsync()
    {
        if (!IPAddress.TryParse(_manual.Text.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            Log("Bitte eine gültige IPv4-Adresse eingeben.");
            return;
        }
        await ConnectAsync(ip, null, new List<string> { ip.ToString() });
    }

    private async Task ConnectAsync(IPAddress address, string? expectedFingerprint, List<string> addresses)
    {
        await DisconnectAsync(null);
        SetBusy(true);
        Log($"Verbinde mit {address}:{Protocol.SessionPort} …");

        var session = new SessionClient(_cert);
        session.RoundTripMeasured += ms => Ui(() => _rtt.Text = $"Umlaufzeit (Ping): {ms:F1} ms");
        session.Disconnected += reason => Ui(async () => await DisconnectAsync($"Verbindung verloren: {reason}"));

        try
        {
            await session.ConnectAsync(address, expectedFingerprint, serverShort => Task.FromResult(InputDialog.AskCode(this,
                "Kopplung",
                "Im Server-Fenster wird jetzt ein 6-stelliger Code angezeigt. Bitte hier eingeben – mit oder ohne Leerzeichen – und mit Enter bestätigen.\n\n" +
                $"Zertifikat des Servers: {serverShort}")));

            _session = session;
            AppSettings.Update(s =>
            {
                s.KnownServers.RemoveAll(k => string.Equals(k.Fingerprint, session.ServerFingerprint, StringComparison.OrdinalIgnoreCase)
                                              || k.Name == session.ServerName);
                s.KnownServers.Add(new KnownServer
                {
                    Fingerprint = session.ServerFingerprint,
                    Name = session.ServerName,
                    Addresses = addresses,
                    LastUsed = DateTime.Now,
                });
            });

            foreach (ListViewItem item in _servers.Items)
                if (item.Tag is FoundServer fs && string.Equals(fs.Info.Fingerprint, session.ServerFingerprint, StringComparison.OrdinalIgnoreCase))
                    item.SubItems[3].Text = "gekoppelt – verbunden";

            _status.Text = $"Verbunden mit {session.ServerName} ({address})";
            Log($"Verbunden mit {session.ServerName}, Zertifikat {Identity.Short(session.ServerFingerprint)}");
            _disconnect.Enabled = true;

            ShackRemote.Core.Media.FfmpegLib.Log -= Log;
            ShackRemote.Core.Media.FfmpegLib.Log += Log;
            var remote = new RemoteForm(session);
            remote.Log += msg => Log(msg);
            remote.FormClosed += async (_, _) =>
            {
                if (ReferenceEquals(_remote, remote))
                {
                    _remote = null;
                    await DisconnectAsync("Fernsteuerungsfenster geschlossen – getrennt.");
                }
            };
            _remote = remote;
            remote.Show();
        }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            _status.Text = "Nicht verbunden";
            Log($"Verbindung fehlgeschlagen: {Explain(ex, address)}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Verständliche Fehlermeldung statt "The operation was canceled".</summary>
    private static string Explain(Exception ex, IPAddress address) => ex switch
    {
        OperationCanceledException =>
            $"{address} antwortet nicht auf Port {Protocol.SessionPort} (5 s gewartet). Mögliche Ursachen: " +
            "ShackRemote läuft dort nicht oder lauscht nicht auf dieser Adresse (im Server-Fenster Haken bei der Adresse setzen), " +
            "Windows-Firewall auf dem Shack-PC, oder Tailscale ist nicht verbunden.",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
            $"{address} lehnt ab: Dort lauscht kein ShackRemote-Server auf Port {Protocol.SessionPort} " +
            "(Server nicht gestartet oder diese Adresse im Server-Fenster abgewählt).",
        SocketException { SocketErrorCode: SocketError.HostUnreachable or SocketError.NetworkUnreachable } =>
            $"{address} ist nicht erreichbar (kein Weg dorthin – anderes Netz? Tailscale aus?).",
        System.Security.Authentication.AuthenticationException => ex.Message,
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    private async Task DisconnectAsync(string? message)
    {
        var r = _remote;
        _remote = null;
        r?.Close();

        var s = _session;
        _session = null;
        if (s is not null) await s.DisposeAsync();
        _disconnect.Enabled = false;
        _status.Text = "Nicht verbunden";
        _rtt.Text = "";
        if (message is not null) Log(message);
    }

    private void SetBusy(bool busy)
    {
        _connect.Enabled = !busy;
        _manualConnect.Enabled = !busy;
        _search.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void Log(string message)
    {
        if (InvokeRequired) { Ui(() => Log(message)); return; }
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(action);
    }
}
