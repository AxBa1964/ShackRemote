using System.Net;
using System.Security.Cryptography.X509Certificates;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>Client-Fenster (Laptop): Server suchen, auswählen, verbinden. Bild folgt in Stufe 1b.</summary>
public sealed class ClientForm : Form
{
    private readonly X509Certificate2 _cert;
    private readonly ListView _servers;
    private readonly Button _search;
    private readonly Button _connect;
    private readonly Button _disconnect;
    private readonly TextBox _manual;
    private readonly Label _status;
    private readonly Label _rtt;
    private readonly TextBox _log;

    private SessionClient? _session;

    public ClientForm(X509Certificate2 cert)
    {
        _cert = cert;

        Text = $"ShackRemote Client {Protocol.Version} – {Environment.MachineName}";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 540);
        MinimumSize = new Size(560, 440);

        _servers = new ListView
        {
            Location = new Point(12, 12),
            Size = new Size(540, 160),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _servers.Columns.Add("Server", 150);
        _servers.Columns.Add("Adresse", 130);
        _servers.Columns.Add("Netz", 90);
        _servers.Columns.Add("Status", 150);
        _servers.DoubleClick += async (_, _) => await ConnectSelectedAsync();

        _search = new Button { Text = "Suchen", Location = new Point(562, 12), Size = new Size(106, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _search.Click += async (_, _) => await SearchAsync();

        _connect = new Button { Text = "Verbinden", Location = new Point(562, 48), Size = new Size(106, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _connect.Click += async (_, _) => await ConnectSelectedAsync();

        _disconnect = new Button { Text = "Trennen", Location = new Point(562, 84), Size = new Size(106, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right, Enabled = false };
        _disconnect.Click += async (_, _) => await DisconnectAsync("Getrennt.");

        var manualLabel = new Label { Text = "Manuell (IPv4):", Location = new Point(12, 185), AutoSize = true };
        _manual = new TextBox { Location = new Point(115, 182), Width = 160 };
        var manualConnect = new Button { Text = "Verbinden", Location = new Point(285, 180), Size = new Size(100, 27) };
        manualConnect.Click += async (_, _) => await ConnectManualAsync();

        _status = new Label { Text = "Nicht verbunden", Location = new Point(12, 220), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _rtt = new Label { Text = "", Location = new Point(12, 245), AutoSize = true };

        _log = new TextBox
        {
            Location = new Point(12, 272),
            Size = new Size(656, 256),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        Controls.AddRange(new Control[] { _servers, _search, _connect, _disconnect, manualLabel, _manual, manualConnect, _status, _rtt, _log });

        Log($"Eigenes Zertifikat: {Identity.Short(Identity.Fingerprint(_cert))}");
        Shown += async (_, _) => await SearchAsync();
        FormClosing += async (_, _) => { if (_session is not null) await _session.DisposeAsync(); };
    }

    private async Task SearchAsync()
    {
        _search.Enabled = false;
        _servers.Items.Clear();
        Log("Suche Server …");
        try
        {
            var known = AppSettings.Read(s => s.KnownServers.ToList());
            var targets = known.SelectMany(k => k.Addresses)
                               .Select(a => IPAddress.TryParse(a, out var ip) ? ip : null)
                               .OfType<IPAddress>();

            var found = await DiscoveryClient.SearchAsync(targets, TimeSpan.FromSeconds(1.5), msg => Log(msg));
            foreach (var f in found.OrderBy(f => f.Info.Name))
            {
                var isKnown = known.Any(k => string.Equals(k.Fingerprint, f.Info.Fingerprint, StringComparison.OrdinalIgnoreCase));
                var kind = f.Info.Addresses.FirstOrDefault(a => a.Address == f.RespondedFrom.ToString())?.Kind ?? "?";
                var item = new ListViewItem(new[]
                {
                    f.Info.Name,
                    f.RespondedFrom.ToString(),
                    kind,
                    isKnown ? "gekoppelt" : "neu – Kopplung nötig",
                }) { Tag = f };
                _servers.Items.Add(item);
            }
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
        // Erwarteter Fingerabdruck: was wir beim letzten Mal unter diesem Namen gespeichert haben.
        var expected = AppSettings.Read(s => s.KnownServers.FirstOrDefault(k => k.Name == f.Info.Name)?.Fingerprint);
        await ConnectAsync(f.RespondedFrom, expected, f.Info.Addresses.Select(a => a.Address).ToList());
    }

    private async Task ConnectManualAsync()
    {
        if (!IPAddress.TryParse(_manual.Text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
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
            await session.ConnectAsync(address, expectedFingerprint, serverShort => Task.FromResult(InputDialog.Ask(this,
                "Kopplung",
                $"Auf dem Shack-PC wird jetzt ein 6-stelliger Code angezeigt. Bitte hier eingeben.\r\nServer-Zertifikat: {serverShort}")));

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

            _status.Text = $"Verbunden mit {session.ServerName} ({address})";
            Log($"Verbunden mit {session.ServerName}, Zertifikat {Identity.Short(session.ServerFingerprint)}");
            _disconnect.Enabled = true;
        }
        catch (Exception ex)
        {
            await session.DisposeAsync();
            _status.Text = "Nicht verbunden";
            Log($"Verbindung fehlgeschlagen: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DisconnectAsync(string? message)
    {
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
