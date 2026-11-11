using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ShackRemote.Core.Media;

/// <summary>
/// Lädt die FFmpeg-Bibliotheken (gemeinsam genutzte DLLs, FFmpeg 9.0.x) im eigenen Prozess.
/// Gesucht wird neben ShackRemote.exe und im Unterordner "ffmpeg".
/// </summary>
public static unsafe class FfmpegLib
{
    private static readonly object Gate = new();
    private static bool _ready;
    private static av_log_set_callback_callback? _logCallback;   // Verweis halten, sonst räumt der GC ihn ab

    /// <summary>FFmpeg-Meldungen (Warnungen und Fehler).</summary>
    public static event Action<string>? Log;

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_ready) return;

            var baseDir = AppContext.BaseDirectory;
            string[] candidates = { baseDir, Path.Combine(baseDir, "ffmpeg") };
            var dir = candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "avcodec-63.dll")))
                ?? throw new FileNotFoundException(
                    "FFmpeg-Bibliotheken (avcodec-63.dll usw., FFmpeg 9.0) nicht gefunden. " +
                    "Bitte tools\\get-ffmpeg.ps1 ausführen oder die DLLs neben ShackRemote.exe legen.");

            ffmpeg.RootPath = dir;
            // Abhängige DLLs (avfilter -> avformat, swscale …) im selben Ordner finden
            SetDllDirectory(dir);

            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_WARNING);
            _logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;
                const int size = 1024;
                var buffer = stackalloc byte[size];
                int printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, buffer, size, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)buffer)?.TrimEnd();
                if (!string.IsNullOrEmpty(line) && Throttle(line)) Log?.Invoke("ffmpeg: " + line);
            };
            ffmpeg.av_log_set_callback(_logCallback);

            Log?.Invoke($"FFmpeg {ffmpeg.av_version_info()} geladen aus {dir}");
            _ready = true;
        }
    }

    private static string? _lastLine;
    private static long _windowStart;
    private static int _windowCount;

    /// <summary>Gleiche Zeilen nicht wiederholen und höchstens 20 Meldungen je 5 s weiterreichen.</summary>
    private static bool Throttle(string line)
    {
        lock (Gate)
        {
            if (line == _lastLine) return false;
            _lastLine = line;
            long now = Environment.TickCount64;
            if (now - _windowStart > 5000) { _windowStart = now; _windowCount = 0; }
            return ++_windowCount <= 20;
        }
    }

    public static string Error(int code)
    {
        const int size = 256;
        var buffer = stackalloc byte[size];
        ffmpeg.av_strerror(code, buffer, size);
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? code.ToString();
    }

    public static void Check(int code, string what)
    {
        if (code < 0) throw new InvalidOperationException($"{what}: {Error(code)} ({code})");
    }

    public static int AVERROR(int errno) => -errno;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
}
