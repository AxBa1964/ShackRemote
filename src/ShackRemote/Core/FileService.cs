using System.Collections.Concurrent;
using ShackRemote.Core.Service;

namespace ShackRemote.Core;

/// <summary>
/// Server: beantwortet Dateianfragen des Clients. Alle Zugriffe im Namen des angemeldeten Benutzers (UserContext).
/// Herunterladen läuft in eigenen Tasks (blockiert die Leseschleife nicht), Hochladen schreibt die Stücke direkt.
/// </summary>
public sealed class FileService : IDisposable
{
    private readonly FramedConnection _conn;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<int, FileStream> _uploads = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _downloads = new();

    public FileService(FramedConnection conn, Action<string> log)
    {
        _conn = conn;
        _log = log;
    }

    /// <summary>Aus der Leseschleife: eine File-Nachricht.</summary>
    public async Task HandleAsync(byte[] payload)
    {
        if (payload.Length == 0) return;
        if (payload[0] == FileProtocol.Chunk)
        {
            int transfer = FileProtocol.ChunkTransfer(payload);
            if (_uploads.TryGetValue(transfer, out var fs))
                await fs.WriteAsync(payload.AsMemory(5));
            return;
        }

        var m = FileProtocol.ParseJson(payload);
        if (m is null) return;
        var reply = new FsMessage { Op = "Result", Id = m.Id, Transfer = m.Transfer, Ok = true };
        try
        {
            switch (m.Op)
            {
                case "Drives":
                    reply.Entries = UserContext.Run(() => FsOps.Drives());
                    break;
                case "List":
                    reply.Entries = UserContext.Run(() => FsOps.List(m.Path!));
                    break;
                case "Mkdir":
                    UserContext.Run(() => Directory.CreateDirectory(m.Path!));
                    _log($"Datei: Ordner angelegt {m.Path}");
                    break;
                case "Delete":
                    UserContext.Run(() => FsOps.Delete(m.Path!));
                    _log($"Datei: gelöscht {m.Path}");
                    break;
                case "Rename":
                    UserContext.Run(() => FsOps.Move(m.Path!, m.Path2!));
                    _log($"Datei: {m.Path} → {m.Path2}");
                    break;
                case "Copy":
                    UserContext.Run(() => FsOps.Copy(m.Path!, m.Path2!));
                    _log($"Datei: kopiert {m.Path} → {m.Path2}");
                    break;
                case "Download":
                {
                    var stream = UserContext.Run(() => new FileStream(m.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                        FileProtocol.ChunkSize, FileOptions.SequentialScan));
                    reply.Size = stream.Length;
                    var cts = new CancellationTokenSource();
                    _downloads[m.Transfer] = cts;
                    await Send(reply);
                    _ = Task.Run(() => DownloadAsync(m.Transfer, stream, cts.Token));
                    return;
                }
                case "Upload":
                {
                    if (!m.Overwrite && UserContext.Run(() => File.Exists(m.Path!) || Directory.Exists(m.Path!)))
                    {
                        reply.Ok = false;
                        reply.Text = "EXISTS";   // Client fragt nach dem Überschreiben
                        break;
                    }
                    var mode = m.Overwrite ? FileMode.Create : FileMode.CreateNew;
                    var stream = UserContext.Run(() => new FileStream(m.Path!, mode, FileAccess.Write, FileShare.None, FileProtocol.ChunkSize));
                    _uploads[m.Transfer] = stream;
                    break;
                }
                case "UploadEnd":
                    if (_uploads.TryRemove(m.Transfer, out var up))
                    {
                        await up.DisposeAsync();
                        if (!m.Ok)   // Client hat abgebrochen: halbe Datei entfernen
                        {
                            UserContext.Run(() => File.Delete(m.Path!));
                            reply.Text = "abgebrochen";
                        }
                        else _log($"Datei: hochgeladen {m.Path}");
                    }
                    break;
                case "Cancel":
                    if (_downloads.TryRemove(m.Transfer, out var c)) c.Cancel();
                    return;
                default:
                    reply.Ok = false;
                    reply.Text = $"Unbekannte Dateianfrage {m.Op}";
                    break;
            }
        }
        catch (Exception ex)
        {
            reply.Ok = false;
            reply.Text = ex.Message;
        }
        await Send(reply);
    }

    private async Task DownloadAsync(int transfer, FileStream stream, CancellationToken ct)
    {
        var end = new FsMessage { Op = "DownloadEnd", Transfer = transfer, Ok = true };
        try
        {
            await using (stream)
            {
                var buffer = new byte[FileProtocol.ChunkSize];
                int n;
                while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                    await _conn.SendAsync(MsgType.File, FileProtocol.BuildChunk(transfer, buffer.AsSpan(0, n)));
            }
        }
        catch (OperationCanceledException)
        {
            end.Ok = false;
            end.Text = "abgebrochen";
        }
        catch (Exception ex)
        {
            end.Ok = false;
            end.Text = ex.Message;
        }
        finally
        {
            _downloads.TryRemove(transfer, out _);
        }
        try { await Send(end); } catch { }
    }

    private Task Send(FsMessage m) => _conn.SendAsync(MsgType.File, FileProtocol.Build(m));

    /// <summary>Bricht laufende Übertragungen ab und schließt offene Dateien.</summary>
    public void Dispose()
    {
        foreach (var c in _downloads.Values) c.Cancel();
        foreach (var u in _uploads.Values) u.Dispose();
        _uploads.Clear();
    }
}
