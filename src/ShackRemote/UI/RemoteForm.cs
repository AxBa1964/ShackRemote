using ShackRemote.Core;
using ShackRemote.Core.Media;

namespace ShackRemote.UI;

/// <summary>
/// Sitzungsfenster (wie bei AnyDesk): Bild des Shack-Monitors, oben Monitorumschaltung und Messwerte.
/// Die Monitorknöpfe stehen in derselben Reihenfolge wie die Bildschirme beim Server (links nach rechts).
/// Maus, Tastatur und Ton folgen in Stufe 1c.
/// </summary>
public sealed class RemoteForm : Form
{
    private readonly SessionClient _session;
    private readonly VideoPanel _video;
    private readonly Label _info;
    private readonly List<(MonitorInfo Monitor, Button Button)> _monitors = new();
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 1000 };

    private VideoRenderer? _renderer;
    private MonitorInfo? _current;
    private long _bytes;
    private double _lastRtt;

    public event Action<string>? Log;

    public RemoteForm(SessionClient session)
    {
        _session = session;

        LayoutKit.PrepareForm(this, $"ShackRemote – {session.ServerName}");
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(480, 320);
        KeyPreview = true;

        // --- Werkzeugleiste: Monitore wie beim Server angeordnet ------------------------
        var list = session.Monitors.Count > 0
            ? session.Monitors.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList()
            : Enumerable.Range(0, 3).Select(i => new MonitorInfo { Index = i, Display = i + 1, Width = 1920, Height = 1080 }).ToList();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 2),
            WrapContents = false,
        };
        var tips = new ToolTip();
        for (int pos = 0; pos < list.Count; pos++)
        {
            var monitor = list[pos];
            var b = LayoutKit.MakeButton($"Monitor {monitor.Display}" + (monitor.Primary ? " ★" : ""));
            b.MinimumSize = new Size(110, 0);
            b.Click += async (_, _) => await SwitchMonitorAsync(monitor);
            tips.SetToolTip(b, $"Strg+{pos + 1} · {monitor.Width} × {monitor.Height} · {monitor.DeviceName}");
            _monitors.Add((monitor, b));
            toolbar.Controls.Add(b);
        }

        _info = LayoutKit.MakeLabel("");
        _info.Margin = new Padding(12, 9, 3, 3);
        toolbar.Controls.Add(_info);

        _video = new VideoPanel { Dock = DockStyle.Fill };

        Controls.Add(_video);
        Controls.Add(toolbar);
        ResumeLayout(true);

        // --- Ereignisse ----------------------------------------------------------------
        _session.VideoData += OnVideoData;
        _session.VideoInfo += OnVideoInfo;
        _session.VideoError += OnVideoError;
        _session.RoundTripMeasured += OnRtt;

        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); _renderer?.Resize(_video.ClientSize.Width, _video.ClientSize.Height); };
        _video.Resize += (_, _) => { _resizeTimer.Stop(); _resizeTimer.Start(); };
        _statsTimer.Tick += (_, _) => UpdateStats();

        // Strg+1..9 = Knopf an dieser Position (von links)
        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
            {
                int pos = e.KeyCode - Keys.D1;
                if (pos < _monitors.Count)
                {
                    e.Handled = true;
                    await SwitchMonitorAsync(_monitors[pos].Monitor);
                }
            }
        };

        Shown += async (_, _) =>
        {
            StartRenderer();
            _statsTimer.Start();
            var first = list.FirstOrDefault(m => m.Primary) ?? list[0];
            await SwitchMonitorAsync(first);
        };

        FormClosing += async (_, _) =>
        {
            _statsTimer.Stop();
            _session.VideoData -= OnVideoData;
            _session.VideoInfo -= OnVideoInfo;
            _session.VideoError -= OnVideoError;
            _session.RoundTripMeasured -= OnRtt;
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
        Log?.Invoke($"Monitor {monitor.Display} angefordert (DXGI-Ausgang {monitor.Index})");
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

    private void OnVideoInfo(int width, int height) => Ui(() =>
        Log?.Invoke($"Shack-Monitor {_current?.Display}: {width} × {height}"));

    private void OnVideoError(string message) => Ui(() =>
    {
        Log?.Invoke($"Server meldet Videofehler: {message}");
        _info.Text = $"Videofehler: {message}";
    });

    private void OnRtt(double ms) => _lastRtt = ms;

    private void UpdateStats()
    {
        long bytes = Interlocked.Exchange(ref _bytes, 0);
        var r = _renderer;
        int decoded = r is null ? 0 : Interlocked.Exchange(ref r.FramesDecoded, 0);
        int shown = r is null ? 0 : Interlocked.Exchange(ref r.FramesShown, 0);
        int resync = r is null ? 0 : Interlocked.Exchange(ref r.Resyncs, 0);
        var src = r is null || r.SourceWidth == 0 ? "–" : $"{r.SourceWidth} × {r.SourceHeight}";
        _info.Text = $"Monitor {_current?.Display} ({src})   {decoded} fps dekodiert / {shown} angezeigt   " +
                     $"{bytes * 8 / 1000} kbit/s   Ping {_lastRtt:F0} ms" +
                     (resync > 0 ? $"   Neusync {resync}×" : "") + "   Strg+1/2/3: Monitor";
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
}
