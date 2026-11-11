using ShackRemote.Core;
using ShackRemote.Core.Media;

namespace ShackRemote.UI;

/// <summary>
/// Sitzungsfenster (wie bei AnyDesk): Bild des Shack-Monitors, oben Monitorumschaltung und Messwerte.
/// Die Monitorknöpfe stehen in derselben Reihenfolge wie die Bildschirme beim Server (links nach rechts).
/// Maus und Tastatur gehen an den Shack-PC (Stufe 1c); Strg+Alt+F1/F2/F3 wechselt lokal den Monitor.
/// </summary>
public sealed class RemoteForm : Form
{
    // Segoe Fluent Icons / MDL2 (Codes laut Microsoft-Liste): TVMonitor, More, KeyboardClassic, Lock
    private const char GlyphMonitor = '\uE7F4', GlyphMore = '\uE712', GlyphKeyboard = '\uE765', GlyphLock = '\uE72E';

    /// <summary>Symbol des Windows-Explorers (aus explorer.exe), passend skaliert; ersatzweise das Ordnersymbol.</summary>
    private static Image ExplorerImage(Control scaleFrom)
    {
        int size = scaleFrom.LogicalToDeviceUnits(20);
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var icon = FilePane.LargeShellIcon(explorer);
        if (icon is null) return Theme.Glyph('\uE8B7', scaleFrom, Theme.Text, 20);
        using var src = icon.ToBitmap();
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, new Rectangle(0, 0, size, size));
        return bmp;
    }

    /// <summary>Farben für das "…"-Menü passend zu Hell/Dunkel.</summary>
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Window;
        public override Color ImageMarginGradientBegin => Theme.Window;
        public override Color ImageMarginGradientMiddle => Theme.Window;
        public override Color ImageMarginGradientEnd => Theme.Window;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }

    private readonly SessionClient _session;
    private readonly VideoPanel _video;
    private readonly Label _info;
    private readonly List<(MonitorInfo Monitor, Button Button)> _monitors = new();
    private readonly Dictionary<MonitorInfo, string> _labels = new();
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 1000 };

    private VideoRenderer? _renderer;
    private AudioPlayer? _audio;
    private FileForm? _files;
    private RemoteFs? _remoteFs;
    private readonly ComboBox _audioSource;
    private MonitorInfo? _current;
    private long _bytes;
    private KeyboardHook? _keyboard;
    private readonly HashSet<(ushort Vk, ushort Scan, bool Ext)> _keysDown = new();
    private readonly HashSet<ushort> _swallowUp = new();
    private bool _ctrl, _alt;
    private ushort _lastX = ushort.MaxValue, _lastY = ushort.MaxValue;
    private double _lastRtt;
    private string _encoder = "";

    public event Action<string>? Log;

    public RemoteForm(SessionClient session)
    {
        _session = session;

        LayoutKit.PrepareForm(this, $"ShackRemote – {session.ServerName}");
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(480, 320);

        // --- Werkzeugleiste: Monitore wie beim Server angeordnet ------------------------
        var list = session.Monitors.Count > 0
            ? session.Monitors.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList()
            : Enumerable.Range(0, 3).Select(i => new MonitorInfo { Index = i, Display = i + 1, Width = 1920, Height = 1080 }).ToList();

        // Kopfleiste: links Monitore, Dateien, Ton – rechts unauffällig "…" mit Strg+Alt+Entf und Sperren
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 2),
            WrapContents = false,
            Margin = new Padding(0),
        };
        // Beschriftung nach Lage (Windows-Nummern weichen von den Anzeigeeinstellungen ab)
        string[] names = list.Count switch
        {
            1 => new[] { "Monitor" },
            2 => new[] { "Links", "Rechts" },
            3 => new[] { "Links", "Mitte", "Rechts" },
            _ => list.Select((_, i) => $"Monitor {i + 1}").ToArray(),
        };
        var tips = new ToolTip();
        for (int pos = 0; pos < list.Count; pos++)
        {
            var monitor = list[pos];
            _labels[monitor] = names[pos];
            var b = LayoutKit.MakeButton(" " + names[pos] + (monitor.Primary ? " ★" : ""));
            b.MinimumSize = new Size(110, 0);
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
            b.Image = Theme.Glyph(GlyphMonitor, b, Theme.Text, 18);   // vor dem Anlegen des Fensters setzen (sonst Neuaufbau während CreateControl -> Fehler 1400)
            b.Click += async (_, _) => await SwitchMonitorAsync(monitor);
            tips.SetToolTip(b, $"Strg+Alt+F{pos + 1} · {monitor.Width} × {monitor.Height}" + (monitor.Primary ? " · Hauptbildschirm" : ""));
            _monitors.Add((monitor, b));
            toolbar.Controls.Add(b);
        }

        var filesButton = LayoutKit.MakeButton(" Dateien");
        filesButton.Margin = new Padding(18, 3, 3, 6);
        filesButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        filesButton.Image = ExplorerImage(filesButton);   // vor dem Anlegen des Fensters setzen (sonst Neuaufbau während CreateControl -> Fehler 1400)
        filesButton.Click += (_, _) => OpenFiles();
        tips.SetToolTip(filesButton, "Dateifenster: Laptop und Shack-PC nebeneinander (kopieren, verschieben, löschen …)");
        toolbar.Controls.Add(filesButton);

        // --- Ton: Quelle beim Shack-PC wählen ------------------------------------------------
        _audioSource = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 260,
            Margin = new Padding(18, 5, 3, 3),
        };
        _audioSource.Items.Add(new AudioChoice(null, "🔇 Ton aus"));
        foreach (var d in session.AudioDevices)
            _audioSource.Items.Add(new AudioChoice(d.Id, (d.Kind == "Aufnahme" ? "🎙 " : "🔊 ") + d.Name));
        tips.SetToolTip(_audioSource,
            "Tonquelle beim Shack-PC: 🔊 = was dort abgespielt wird, 🎙 = Aufnahmegerät (z. B. USB-Soundkarte des Funkgeräts)");
        toolbar.Controls.Add(_audioSource);

        // Messwerte in eigener Statuszeile unten (in der Leiste wurden sie abgeschnitten)
        _info = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.SubtleText };
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(26), Padding = new Padding(10, 0, 10, 0), BackColor = Theme.Surface };
        statusBar.Controls.Add(_info);

        // --- Rechts: "…"-Menü für seltene Aktionen --------------------------------------------
        var more = new Button
        {
            Size = new Size(40, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface,
            Margin = new Padding(3, 4, 8, 4),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Tag = "themed",
        };
        more.FlatAppearance.BorderSize = 0;
        more.FlatAppearance.MouseOverBackColor = Theme.Hover;
        more.FlatAppearance.MouseDownBackColor = Theme.Pressed;
        more.Image = Theme.Glyph(GlyphMore, more, Theme.SubtleText, 18);   // vor dem Anlegen des Fensters setzen (sonst Neuaufbau während CreateControl -> Fehler 1400)
        tips.SetToolTip(more, "Weitere Aktionen: Strg+Alt+Entf, Shack-PC sperren");

        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false }, BackColor = Theme.Window, ShowImageMargin = true };
        ToolStripMenuItem Item(string text, char glyph, string tip, Func<Task> action)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = Theme.Text, ToolTipText = tip, Padding = new Padding(0, 3, 0, 3) };
            item.Click += async (_, _) => await action();
            item.Image = Theme.Glyph(glyph, more, Theme.Text, 16);
            return item;
        }
        menu.Items.Add(Item("Strg+Alt+Entf senden", GlyphKeyboard,
            "Strg+Alt+Entf auf dem Shack-PC (z. B. zum Entsperren). Braucht den Dienst auf dem Shack-PC.", () => CommandAsync("SendSas")));
        menu.Items.Add(Item("Shack-PC sperren", GlyphLock,
            "Shack-PC sperren (Win+L wirkt immer nur auf den Laptop).", () => CommandAsync("Lock")));
        more.Click += (_, _) => menu.Show(more, new Point(more.Width - menu.PreferredSize.Width, more.Height));

        var head = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Surface };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        head.Controls.Add(toolbar, 0, 0);
        head.Controls.Add(more, 1, 0);

        _video = new VideoPanel { Dock = DockStyle.Fill };

        Controls.Add(_video);
        Controls.Add(head);
        Controls.Add(statusBar);
        ResumeLayout(true);

        // --- Ereignisse ----------------------------------------------------------------
        _session.VideoData += OnVideoData;
        _session.VideoInfo += OnVideoInfo;
        _session.VideoError += OnVideoError;
        _session.RoundTripMeasured += OnRtt;
        _session.Info += OnInfo;
        _session.AudioData += OnAudioData;

        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); _renderer?.Resize(_video.ClientSize.Width, _video.ClientSize.Height); };
        _video.Resize += (_, _) => { _resizeTimer.Stop(); _resizeTimer.Start(); };
        _statsTimer.Tick += (_, _) => UpdateStats();

        // --- Maus über dem Bild -> Shack-PC ----------------------------------------------
        _video.MouseMove += (_, e) => SendMouseMove(e.Location);
        _video.MouseDown += (_, e) => { _video.Focus(); SendMouseButton(e, true); };
        _video.MouseUp += (_, e) => SendMouseButton(e, false);
        _video.MouseWheel += (_, e) => _session.SendInput(InputProtocol.Wheel((short)Math.Clamp(e.Delta, short.MinValue, short.MaxValue), false));
        _video.HorizontalWheel += delta => _session.SendInput(InputProtocol.Wheel(delta, true));
        _video.MouseEnter += (_, _) => { if (ContainsFocus) _video.Focus(); };

        // --- Tastatur: globaler Haken, solange dieses Fenster vorne ist ------------------------
        Deactivate += (_, _) => ReleaseRemoteKeys();

        Shown += async (_, _) =>
        {
            try
            {
                _keyboard = new KeyboardHook { IsActive = () => ActiveForm == this && WindowState != FormWindowState.Minimized, KeyEvent = OnKey };
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Tastatur kann nicht übertragen werden: {ex.Message}");
            }
            StartRenderer();
            _statsTimer.Start();
            SelectRememberedAudio();
            _audioSource.SelectedIndexChanged += async (_, _) => await ApplyAudioAsync();
            await ApplyAudioAsync();
            var first = list.FirstOrDefault(m => m.Primary) ?? list[0];
            await SwitchMonitorAsync(first);
        };

        FormClosing += async (_, _) =>
        {
            _statsTimer.Stop();
            ReleaseRemoteKeys();
            _keyboard?.Dispose();
            _keyboard = null;
            _session.VideoData -= OnVideoData;
            _session.VideoInfo -= OnVideoInfo;
            _session.VideoError -= OnVideoError;
            _session.RoundTripMeasured -= OnRtt;
            _session.Info -= OnInfo;
            _session.AudioData -= OnAudioData;
            _files?.Close();
            _remoteFs?.Dispose();
            _audio?.Dispose();
            _audio = null;
            var r = _renderer;
            _renderer = null;
            _video.Renderer = null;
            r?.Dispose();
            try { await _session.StopVideoAsync(); } catch { /* Verbindung evtl. schon weg */ }
        };
    }

    private void StartRenderer()
    {
        try
        {
            var r = new VideoRenderer(_video.Handle, _video.ClientSize.Width, _video.ClientSize.Height);
            r.Log += msg => Ui(() => Log?.Invoke(msg));
            r.KeyframeNeeded += () => Ui(async () =>
            {
                try { await _session.RequestKeyframeAsync(); } catch { }
            });
            r.Start();
            _renderer = r;
            _video.Renderer = r;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Anzeige kann nicht starten: {ex.Message}");
            _info.Text = ex.Message;
        }
    }

    private async Task SwitchMonitorAsync(MonitorInfo monitor)
    {
        _current = monitor;
        foreach (var (m, b) in _monitors)
            b.Font = new Font(Font, ReferenceEquals(m, monitor) ? FontStyle.Bold : FontStyle.Regular);
        Log?.Invoke($"Monitor {Label(monitor)} angefordert (DXGI-Ausgang {monitor.Index})");
        _renderer?.Reset();
        try
        {
            await _session.StartVideoAsync(monitor.Index);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Monitorwechsel fehlgeschlagen: {ex.Message}");
        }
    }

    // ---- Aus der Leseschleife der Sitzung (Hintergrund-Thread) ---------------------------

    private void OnVideoData(byte[] data, bool key)
    {
        Interlocked.Add(ref _bytes, data.Length);
        _renderer?.Submit(data, key);
    }

    private void OnVideoInfo(int width, int height, string? encoder) => Ui(() =>
    {
        _encoder = encoder ?? "";
        Log?.Invoke($"Shack-Monitor {Label(_current)}: {width} × {height}, Encoder {encoder}");
    });

    private void OnVideoError(string message) => Ui(() =>
    {
        Log?.Invoke($"Server meldet Videofehler: {message}");
        _info.Text = $"Videofehler: {message}";
    });

    private void OnRtt(double ms) => _lastRtt = ms;

    private sealed record AudioChoice(string? Id, string Text)
    {
        public override string ToString() => Text;
    }

    private void SelectRememberedAudio()
    {
        var wanted = AppSettings.Read(st => st.AudioSource);
        int index = 0;
        for (int i = 0; i < _audioSource.Items.Count; i++)
            if (((AudioChoice)_audioSource.Items[i]!).Id == wanted) { index = i; break; }
        _audioSource.SelectedIndex = index;
    }

    /// <summary>Gewählte Tonquelle beim Server anfordern und hier abspielen.</summary>
    private async Task ApplyAudioAsync()
    {
        var choice = _audioSource.SelectedItem as AudioChoice;
        AppSettings.Update(st => st.AudioSource = choice?.Id);
        try
        {
            if (choice?.Id is null)
            {
                await _session.StopAudioAsync();
                _audio?.Dispose();
                _audio = null;
                return;
            }
            if (_audio is null)
            {
                var player = new AudioPlayer();
                player.Log += msg => Ui(() => Log?.Invoke(msg));
                player.Start();
                _audio = player;
            }
            await _session.StartAudioAsync(choice.Id);
            Log?.Invoke($"Ton angefordert: {choice.Text}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Ton nicht möglich: {ex.Message}");
        }
        _video.Focus();
    }

    private void OpenFiles()
    {
        if (_files is { IsDisposed: false })
        {
            _files.WindowState = FormWindowState.Normal;
            _files.Activate();
            return;
        }
        _remoteFs ??= new RemoteFs(_session);
        _files = new FileForm(_session, _remoteFs);
        _files.Show();
    }

    private void OnAudioData(byte[] data) => _audio?.Submit(data);

    private void OnInfo(string text) => Ui(() => Log?.Invoke($"Shack-PC: {text}"));

    private async Task CommandAsync(string type)
    {
        try { await _session.SendCommandAsync(type); }
        catch (Exception ex) { Log?.Invoke($"Befehl {type} fehlgeschlagen: {ex.Message}"); }
        _video.Focus();
    }

    private void UpdateStats()
    {
        long bytes = Interlocked.Exchange(ref _bytes, 0);
        var r = _renderer;
        int decoded = r is null ? 0 : Interlocked.Exchange(ref r.FramesDecoded, 0);
        int shown = r is null ? 0 : Interlocked.Exchange(ref r.FramesShown, 0);
        int resync = r is null ? 0 : Interlocked.Exchange(ref r.Resyncs, 0);
        var src = r is null || r.SourceWidth == 0 ? "–" : $"{r.SourceWidth} × {r.SourceHeight}";
        _info.Text = $"{Label(_current)} ({src})   {decoded} fps dekodiert / {shown} angezeigt   " +
                     $"{bytes * 8 / 1000} kbit/s   Ping {_lastRtt:F0} ms" +
                     (resync > 0 ? $"   Neusync {resync}×" : "") +
                     (_encoder.Length > 0 ? $"   {_encoder}" : "") +
                     (_audio is not null ? $"   Ton-Puffer {_audio.Buffered.TotalMilliseconds:F0} ms" : "");
    }

    // ---- Eingabe ------------------------------------------------------------------------------

    /// <summary>Fensterpunkt -> 0..65535 relativ zum gezeigten Bild (außerhalb wird auf den Rand begrenzt).</summary>
    private bool ToRemote(Point p, out ushort x, out ushort y)
    {
        x = y = 0;
        var b = _renderer?.ImageBounds ?? Rectangle.Empty;
        if (b.Width < 2 || b.Height < 2) return false;
        x = (ushort)Math.Clamp((long)(p.X - b.X) * 65535 / (b.Width - 1), 0, 65535);
        y = (ushort)Math.Clamp((long)(p.Y - b.Y) * 65535 / (b.Height - 1), 0, 65535);
        return true;
    }

    private void SendMouseMove(Point p)
    {
        if (!ToRemote(p, out var x, out var y) || (x == _lastX && y == _lastY)) return;
        _lastX = x; _lastY = y;
        _session.SendInput(InputProtocol.Move(x, y));
    }

    private void SendMouseButton(MouseEventArgs e, bool down)
    {
        byte? button = e.Button switch
        {
            MouseButtons.Left => (byte)0, MouseButtons.Right => (byte)1, MouseButtons.Middle => (byte)2,
            MouseButtons.XButton1 => (byte)3, MouseButtons.XButton2 => (byte)4, _ => (byte?)null,
        };
        if (button is null || !ToRemote(e.Location, out var x, out var y)) return;
        _session.SendInput(InputProtocol.Button(button.Value, down, x, y));
    }

    /// <summary>Aus dem Tastatur-Haken (UI-Thread). true = Taste lokal verschlucken.</summary>
    private bool OnKey(ushort vk, ushort scan, bool up, bool extended)
    {
        // Strg/Alt mitverfolgen (für das lokale Kürzel); VK_CONTROL/LCONTROL/RCONTROL, VK_MENU/LMENU/RMENU
        if (vk is 0x11 or 0xA2 or 0xA3) _ctrl = !up;
        if (vk is 0x12 or 0xA4 or 0xA5) _alt = !up;

        // Lokales Kürzel Strg+Alt+F1..F9: Monitor wechseln, nicht senden (F1 = 0x70)
        if (vk >= 0x70 && vk <= 0x78)
        {
            int pos = vk - 0x70;
            if (!up && _ctrl && _alt && pos < _monitors.Count)
            {
                _swallowUp.Add(vk);
                var monitor = _monitors[pos].Monitor;
                BeginInvoke(new Action(async () => await SwitchMonitorAsync(monitor)));
                return true;
            }
            if (up && _swallowUp.Remove(vk)) return true;
        }

        var key = (vk, scan, extended);
        if (up) _keysDown.Remove(key); else _keysDown.Add(key);
        _session.SendInput(InputProtocol.KeyEvent(vk, scan, up, extended));
        return true;
    }

    /// <summary>Fenster verliert den Fokus: beim Shack-PC alles loslassen, sonst "klemmt" z. B. Alt.</summary>
    private void ReleaseRemoteKeys()
    {
        foreach (var (vk, scan, ext) in _keysDown)
            _session.SendInput(InputProtocol.KeyEvent(vk, scan, true, ext));
        _keysDown.Clear();
        _swallowUp.Clear();
        _ctrl = _alt = false;
    }

    private string Label(MonitorInfo? m) => m is not null && _labels.TryGetValue(m, out var l) ? l : "–";

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
}
