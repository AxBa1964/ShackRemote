using System.Diagnostics;
using FFmpeg.AutoGen;

namespace ShackRemote.Core.Media;

/// <summary>
/// Server: Aufnahme und Kodierung im eigenen Prozess, alles auf der GPU (wie Sunshine/Parsec):
/// ddagrab (Desktop Duplication, D3D11-Textur) -> hwmap nach QSV -> h264_qsv.
/// Schlüsselbilder auf Anforderung (RequestKeyframe) statt Encoder-Neustart.
/// Jedes fertige Paket geht als ein Stück an <see cref="PacketReady"/>.
/// </summary>
public sealed unsafe class ScreenEncoder : IDisposable
{
    private readonly int _output;
    private readonly int _fps;
    private readonly Thread _thread;
    private volatile bool _stop;
    private int _keyframeRequested = 1;   // erstes Bild immer IDR

    /// <summary>Kodiertes Paket (Annex B, SPS/PPS vor jedem Schlüsselbild) und ob es ein Schlüsselbild ist.</summary>
    public event Action<byte[], bool>? PacketReady;
    public event Action<int, int>? Started;      // Breite, Höhe
    public event Action<string>? Log;
    public event Action<string>? Failed;

    public ScreenEncoder(int output, int fps)
    {
        _output = output;
        _fps = fps;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Encoder Ausgang {output}", Priority = ThreadPriority.AboveNormal };
    }

    public void Start()
    {
        FfmpegLib.EnsureLoaded();
        _thread.Start();
    }

    public void RequestKeyframe() => Interlocked.Exchange(ref _keyframeRequested, 1);

    private void Run()
    {
        AVBufferRef* device = null;
        AVFilterGraph* graph = null;
        AVCodecContext* enc = null;
        AVBSFContext* bsf = null;
        AVFrame* frame = ffmpeg.av_frame_alloc();
        AVPacket* packet = ffmpeg.av_packet_alloc();
        try
        {
            // --- D3D11-Gerät auf der Intel-GPU (wie "-init_hw_device d3d11va:,vendor_id=0x8086") ---
            AVDictionary* opts = null;
            ffmpeg.av_dict_set(&opts, "vendor_id", "0x8086", 0);
            int ret = ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, opts, 0);
            ffmpeg.av_dict_free(&opts);
            if (ret < 0)
            {
                Log?.Invoke($"Video: Intel-GPU nicht direkt gefunden ({FfmpegLib.Error(ret)}), nehme Standard-GPU");
                FfmpegLib.Check(ffmpeg.av_hwdevice_ctx_create(&device, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0), "D3D11-Gerät");
            }

            // --- Filtergraph: ddagrab -> hwmap(qsv) -> format=qsv -> buffersink ---
            graph = ffmpeg.avfilter_graph_alloc();
            AVFilterContext* sink;
            FfmpegLib.Check(ffmpeg.avfilter_graph_create_filter(&sink, ffmpeg.avfilter_get_by_name("buffersink"), "out", null, null, graph), "buffersink");

            AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();
            inputs->name = ffmpeg.av_strdup("out");
            inputs->filter_ctx = sink;
            inputs->pad_idx = 0;
            inputs->next = null;
            AVFilterInOut* outputs = null;
            var description = $"ddagrab=output_idx={_output}:framerate={_fps}:draw_mouse=1,hwmap=derive_device=qsv,format=qsv";
            ret = ffmpeg.avfilter_graph_parse_ptr(graph, description, &inputs, &outputs, null);
            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
            FfmpegLib.Check(ret, "Filtergraph");

            // Gerät allen Filtern geben (ddagrab nutzt es beim Konfigurieren)
            for (uint i = 0; i < graph->nb_filters; i++)
            {
                var f = graph->filters[i];
                if (f->hw_device_ctx == null) f->hw_device_ctx = ffmpeg.av_buffer_ref(device);
            }
            FfmpegLib.Check(ffmpeg.avfilter_graph_config(graph, null), $"Bildschirmausgang {_output} öffnen");

            int width = ffmpeg.av_buffersink_get_w(sink);
            int height = ffmpeg.av_buffersink_get_h(sink);
            var timeBase = ffmpeg.av_buffersink_get_time_base(sink);

            // --- Encoder h264_qsv, Einstellungen wie in Stufe 0 getestet ---
            var codec = ffmpeg.avcodec_find_encoder_by_name("h264_qsv");
            if (codec == null) throw new InvalidOperationException("h264_qsv fehlt in dieser FFmpeg-Version.");
            enc = ffmpeg.avcodec_alloc_context3(codec);
            enc->width = width;
            enc->height = height;
            enc->pix_fmt = AVPixelFormat.AV_PIX_FMT_QSV;
            enc->time_base = timeBase;
            enc->framerate = new AVRational { num = _fps, den = 1 };
            enc->gop_size = _fps * 2;             // regulär alle 2 s, sonst auf Anforderung
            enc->max_b_frames = 0;
            enc->global_quality = 20;             // ICQ wie in Stufe 0
            enc->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            enc->hw_frames_ctx = ffmpeg.av_buffer_ref(ffmpeg.av_buffersink_get_hw_frames_ctx(sink));
            ffmpeg.av_opt_set(enc->priv_data, "async_depth", "1", 0);
            ffmpeg.av_opt_set(enc->priv_data, "forced_idr", "1", 0);
            FfmpegLib.Check(ffmpeg.avcodec_open2(enc, codec, null), "h264_qsv öffnen");

            // --- SPS/PPS vor jedes Schlüsselbild (dump_extra), damit der Client jederzeit einsteigen kann ---
            FfmpegLib.Check(ffmpeg.av_bsf_alloc(ffmpeg.av_bsf_get_by_name("dump_extra"), &bsf), "dump_extra");
            FfmpegLib.Check(ffmpeg.avcodec_parameters_from_context(bsf->par_in, enc), "dump_extra Parameter");
            bsf->time_base_in = enc->time_base;
            FfmpegLib.Check(ffmpeg.av_bsf_init(bsf), "dump_extra init");

            Log?.Invoke($"Video: Ausgang {_output}, {width} × {height}, {_fps} fps, h264_qsv im Prozess");
            Started?.Invoke(width, height);

            long bytes = 0, frames = 0, keys = 0;
            var report = Stopwatch.StartNew();

            while (!_stop)
            {
                ret = ffmpeg.av_buffersink_get_frame(sink, frame);
                if (ret == FfmpegLib.AVERROR(ffmpeg.EAGAIN)) continue;
                FfmpegLib.Check(ret, "Aufnahme");

                if (Interlocked.Exchange(ref _keyframeRequested, 0) == 1)
                {
                    frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
                    frame->flags |= ffmpeg.AV_FRAME_FLAG_KEY;
                }
                else
                {
                    frame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
                }

                ret = ffmpeg.avcodec_send_frame(enc, frame);
                ffmpeg.av_frame_unref(frame);
                FfmpegLib.Check(ret, "Kodieren");

                while (ffmpeg.avcodec_receive_packet(enc, packet) >= 0)
                {
                    FfmpegLib.Check(ffmpeg.av_bsf_send_packet(bsf, packet), "dump_extra");
                    while (ffmpeg.av_bsf_receive_packet(bsf, packet) >= 0)
                    {
                        bool key = (packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                        var data = new byte[packet->size];
                        new ReadOnlySpan<byte>(packet->data, packet->size).CopyTo(data);
                        ffmpeg.av_packet_unref(packet);

                        bytes += data.Length;
                        frames++;
                        if (key) keys++;
                        PacketReady?.Invoke(data, key);
                    }
                }

                if (report.Elapsed >= TimeSpan.FromSeconds(5))
                {
                    Log?.Invoke($"Video: {frames / report.Elapsed.TotalSeconds:F0} fps, {bytes * 8 / 1000 / report.Elapsed.TotalSeconds:F0} kbit/s, {keys} Schlüsselbilder");
                    bytes = 0; frames = 0; keys = 0;
                    report.Restart();
                }
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Failed?.Invoke(ex.Message);
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
            if (bsf != null) ffmpeg.av_bsf_free(&bsf);
            if (enc != null) ffmpeg.avcodec_free_context(&enc);
            if (graph != null) ffmpeg.avfilter_graph_free(&graph);
            if (device != null) ffmpeg.av_buffer_unref(&device);
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(3));
    }
}
