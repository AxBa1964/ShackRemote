using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Sitzungsfenster (wie bei AnyDesk): Bild des Shack-Monitors, oben Monitorumschaltung und Messwerte.
/// Stufe 1b: nur Anzeige. Maus, Tastatur und Ton folgen in Stufe 1c.
/// </summary>
public sealed class RemoteForm : Form
{
    private readonly SessionClient _session;
    private readonly VideoPanel _video;
    private readonly Label _info;
    private readonly Button[] _monitorButtons;
    private readonly System.Windows.Forms.Timer _resizeTimer = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 1000 };

    private VideoDecoder? _decoder;
    private int _sourceWidth = 1920, _sourceHeight = 1080;   // bis VideoInfo vom Server kommt
    private int _monitor;
    private long _bytes;
    private int _framesDecoded;
    private double _lastRtt;

    public event Action<string>? Log;

    public RemoteForm(SessionClient session)
    {
        _session = session;

        LayoutKit.PrepareForm(this, $"ShackRemote – {session.ServerName}");
        ClientSize = new Size(1280, 760);
        MinimumSize = new Size(480, 320);
        KeyPreview = true;

        // --- Werkzeugleiste ----------------------------------------------------------
        _monitorButtons = Enumerable.Range(0, 3).Select(i =>
        {
            var b = LayoutKit.MakeButton($"Monitor {i + 1}");
            b.MinimumSize = new Size(100, 0);
            b.Click += async (_, _) => await SwitchMonitorAsync(i);
            return b;
        }).ToArray();

        _info = LayoutKit.MakeLabel("");
        _info.Margin = new Padding(12, 9, 3, 3);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 2),
            WrapContents = false,
        };
        toolbar.Controls.AddRange(_monitorButtons);
        toolbar.Controls.Add(_info);

        _video = new VideoPanel { Dock = DockStyle.Fill };

        Controls.Add(_video);
        Controls.Add(toolbar);
        ResumeLayout(true);

        // --- Ereignisse ----------------------------------------------------------------
        _session.VideoData += OnVideoData;
        _session.VideoReset += OnVideoReset;
        _session.VideoInfo += OnVideoInfo;
        _session.VideoError += OnVideoError;
        _session.RoundTripMeasured += OnRtt;

        _resizeTimer.Tick += (_, _) => { _resizeTimer.Stop(); RestartDecoderIfSizeChanged(); };
        _video.Resize += (_, _) => { _resizeTimer.Stop(); _resizeTimer.Start(); };
        _statsTimer.Tick += (_, _) => UpdateStats();

        // Strg+1/2/3 wechselt den Monitor (Tastenkürzel auch im Vollbild nutzbar)
        KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode is Keys.D1 or Keys.D2 or Keys.D3)
            {
                e.Handled = true;
                await SwitchMonitorAsync(e.KeyCode - Keys.D1);
            }
        };

        Shown += async (_, _) =>
        {
            _statsTimer.Start();
            await SwitchMonitorAsync(0);
        };

        FormClosing += async (_, _) =>
        {
            _statsTimer.Stop();
            _session.VideoData -= OnVideoData;
            _session.VideoReset -= OnVideoReset;
            _session.VideoInfo -= OnVideoInfo;
            _session.VideoError -= OnVideoError;
            _session.RoundTripMeasured -= OnRtt;
            _decoder?.Dispose();
            _decoder = null;
            try { await _session.StopVideoAsync(); } catch { /* Verbindung evtl. schon weg */ }
        };
    }

    private async Task SwitchMonitorAsync(int monitor)
    {
        _monitor = monitor;
        for (int i = 0; i < _monitorButtons.Length; i++)
            _monitorButtons[i].Font = new Font(Font, i == monitor ? FontStyle.Bold : FontStyle.Regular);
        Log?.Invoke($"Monitor {monitor + 1} angefordert");
        try
        {
            await _session.StartVideoAsync(monitor);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Monitorwechsel fehlgeschlagen: {ex.Message}");
        }
    }

    // ---- Aus der Leseschleife der Sitzung (Hintergrund-Thread) ---------------------------

    private void OnVideoData(byte[] data)
    {
        Interlocked.Add(ref _bytes, data.Length);
        _decoder?.Write(data);
    }

    private void OnVideoReset(int monitor) => Ui(() =>
    {
        // Neuer Datenstrom vom Server: Decoder neu starten, damit er sauber am Anfang einsteigt.
        _video.Clear();
        RestartDecoder();
    });

    private void OnVideoInfo(int width, int height) => Ui(() =>
    {
        bool changed = width != _sourceWidth || height != _sourceHeight;
        _sourceWidth = width;
        _sourceHeight = height;
        Log?.Invoke($"Shack-Monitor {_monitor + 1}: {width} × {height}");
        if (changed) RestartDecoder();
    });

    private void OnVideoError(string message) => Ui(() =>
    {
        Log?.Invoke($"Server meldet Videofehler: {message}");
        _info.Text = $"Videofehler: {message}";
    });

    private void OnRtt(double ms) => _lastRtt = ms;

    // ---- Decoder ------------------------------------------------------------------------------

    private Size TargetSize()
    {
        var area = _video.ClientSize;
        if (area.Width < 16 || area.Height < 16) return new Size(320, 180);
        double scale = Math.Min((double)area.Width / _sourceWidth, (double)area.Height / _sourceHeight);
        // Nicht über die Originalgröße hinaus vergrößern: kostet nur Rechenzeit und bringt keine Schärfe.
        scale = Math.Min(scale, 1.0);
        int w = Math.Max(64, (int)(_sourceWidth * scale)) & ~1;   // gerade Werte
        int h = Math.Max(64, (int)(_sourceHeight * scale)) & ~1;
        return new Size(w, h);
    }

    private void RestartDecoderIfSizeChanged()
    {
        var target = TargetSize();
        if (_decoder is null || _decoder.Width != target.Width || _decoder.Height != target.Height)
            RestartDecoder();
    }

    private void RestartDecoder()
    {
        _decoder?.Dispose();
        var size = TargetSize();
        try
        {
            var d = new VideoDecoder(size.Width, size.Height);
            d.FrameReady += (frame, w, h) =>
            {
                Interlocked.Increment(ref _framesDecoded);
                _video.UpdateFrame(frame, w, h);
            };
            d.Log += msg => Ui(() => Log?.Invoke(msg));
            _decoder = d;
        }
        catch (Exception ex)
        {
            _decoder = null;
            Log?.Invoke($"Decoder kann nicht starten: {ex.Message}");
            _info.Text = ex.Message;
        }
    }

    private void UpdateStats()
    {
        long bytes = Interlocked.Exchange(ref _bytes, 0);
        int frames = Interlocked.Exchange(ref _framesDecoded, 0);
        int drops = _decoder?.TakeDrops() ?? 0;
        var size = _decoder is null ? "–" : $"{_decoder.Width} × {_decoder.Height}";
        _info.Text = $"Monitor {_monitor + 1} ({_sourceWidth} × {_sourceHeight})   Anzeige {size}   " +
                     $"{frames} fps   {bytes * 8 / 1000} kbit/s   Ping {_lastRtt:F0} ms" +
                     (drops > 0 ? $"   Neusync {drops}×" : "") + "   Strg+1/2/3: Monitor";
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }
}
