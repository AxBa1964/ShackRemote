using System.Collections.Concurrent;

namespace ShackRemote.Core;

/// <summary>
/// Client: Dateizugriff auf den Shack-PC über die bestehende Sitzung (Anfrage/Antwort mit Id, Übertragungen in Stücken).
/// </summary>
public sealed class RemoteFs : IDisposable
{
    private readonly SessionClient _session;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<FsMessage>> _pending = new();
    private readonly ConcurrentDictionary<int, Download> _downloads = new();
    private int _nextId;

    private sealed class Download
    {
        public required FileStream Target;
        public required TaskCompletionSource<FsMessage> Done;
        public Action<long>? Progress;
        public long Received;
    }

    public RemoteFs(SessionClient session)
    {
        _session = session;
        _session.FileData += OnData;
    }

    public void Dispose() => _session.FileData -= OnData;

    // ---- Aus der Leseschleife --------------------------------------------------------------------

    private void OnData(byte[] payload)
    {
        if (payload.Length == 0) return;
        if (payload[0] == FileProtocol.Chunk)
        {
            if (_downloads.TryGetValue(FileProtocol.ChunkTransfer(payload), out var d))
            {
                d.Target.Write(payload, 5, payload.Length - 5);
                d.Received += payload.Length - 5;
                d.Progress?.Invoke(d.Received);
            }
            return;
        }
        var m = FileProtocol.ParseJson(payload);
        if (m is null) return;
        if (m.Op == "DownloadEnd")
        {
            if (_downloads.TryRemove(m.Transfer, out var d)) d.Done.TrySetResult(m);
        }
        else if (_pending.TryRemove(m.Id, out var tcs))
        {
            tcs.TrySetResult(m);
        }
    }

    // ---- Anfragen ----------------------------------------------------------------------------------

    private async Task<FsMessage> RequestAsync(FsMessage m)
    {
        m.Id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<FsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[m.Id] = tcs;
        await _session.SendFileAsync(FileProtocol.Build(m));
        var reply = await tcs.Task.WaitAsync(TimeSpan.FromMinutes(5));
        if (!reply.Ok) throw new IOException(reply.Text ?? "Fehler beim Shack-PC");
        return reply;
    }

    public async Task<List<FsEntry>> DrivesAsync() => (await RequestAsync(new FsMessage { Op = "Drives" })).Entries ?? new();
    public async Task<List<FsEntry>> ListAsync(string path) => (await RequestAsync(new FsMessage { Op = "List", Path = path })).Entries ?? new();
    public Task MkdirAsync(string path) => RequestAsync(new FsMessage { Op = "Mkdir", Path = path });
    public Task DeleteAsync(string path) => RequestAsync(new FsMessage { Op = "Delete", Path = path });
    public Task RenameAsync(string from, string to) => RequestAsync(new FsMessage { Op = "Rename", Path = from, Path2 = to });
    public Task CopyAsync(string from, string to) => RequestAsync(new FsMessage { Op = "Copy", Path = from, Path2 = to });

    /// <summary>Datei vom Shack-PC holen. progress: bisher empfangene Bytes.</summary>
    public async Task DownloadAsync(string remote, string local, bool overwrite, Action<long>? progress, CancellationToken ct)
    {
        int transfer = Interlocked.Increment(ref _nextId);
        var target = new FileStream(local, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, FileProtocol.ChunkSize);
        var d = new Download { Target = target, Done = new(TaskCreationOptions.RunContinuationsAsynchronously), Progress = progress };
        _downloads[transfer] = d;
        bool ok = false;
        try
        {
            await RequestAsync(new FsMessage { Op = "Download", Path = remote, Transfer = transfer });
            using (ct.Register(() => _ = _session.SendFileAsync(FileProtocol.Build(new FsMessage { Op = "Cancel", Transfer = transfer }))))
            {
                var end = await d.Done.Task;
                if (!end.Ok) throw new IOException(end.Text ?? "Übertragung fehlgeschlagen");
            }
            ok = true;
        }
        finally
        {
            _downloads.TryRemove(transfer, out _);
            await target.DisposeAsync();
            if (!ok) try { File.Delete(local); } catch { }
        }
    }

    /// <summary>Datei zum Shack-PC schicken. progress: bisher gesendete Bytes.</summary>
    public async Task UploadAsync(string local, string remote, bool overwrite, Action<long>? progress, CancellationToken ct)
    {
        int transfer = Interlocked.Increment(ref _nextId);
        await using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileProtocol.ChunkSize, FileOptions.SequentialScan);
        await RequestAsync(new FsMessage { Op = "Upload", Path = remote, Transfer = transfer, Overwrite = overwrite });
        bool ok = false;
        try
        {
            var buffer = new byte[FileProtocol.ChunkSize];
            long sent = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await _session.SendFileAsync(FileProtocol.BuildChunk(transfer, buffer.AsSpan(0, n)));
                sent += n;
                progress?.Invoke(sent);
            }
            ok = true;
        }
        finally
        {
            await RequestAsync(new FsMessage { Op = "UploadEnd", Path = remote, Transfer = transfer, Ok = ok });
        }
    }
}
