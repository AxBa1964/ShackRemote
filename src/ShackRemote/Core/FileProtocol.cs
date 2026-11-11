using System.Buffers.Binary;
using System.Text.Json;

namespace ShackRemote.Core;

/// <summary>
/// Dateizugriff (MsgType.File). Nutzlast:
///   [1][JSON FsMessage]                 Anfrage/Antwort
///   [2][Transfer i32][Daten]            Datenstück einer Übertragung (max. 64 KB, damit das Bild nicht stockt)
/// Anfragen (Client → Server): Drives, List, Mkdir, Delete, Rename, Copy, Download, Upload, UploadEnd, Cancel.
/// Antworten (Server → Client): Result (zu Id), DownloadEnd (zu Transfer).
/// </summary>
public static class FileProtocol
{
    public const byte Json = 1, Chunk = 2;
    public const int ChunkSize = 64 * 1024;

    public static byte[] Build(FsMessage m)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(m);
        var b = new byte[json.Length + 1];
        b[0] = Json;
        json.CopyTo(b, 1);
        return b;
    }

    public static byte[] BuildChunk(int transfer, ReadOnlySpan<byte> data)
    {
        var b = new byte[5 + data.Length];
        b[0] = Chunk;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), transfer);
        data.CopyTo(b.AsSpan(5));
        return b;
    }

    public static FsMessage? ParseJson(byte[] payload) =>
        JsonSerializer.Deserialize<FsMessage>(payload.AsSpan(1));

    public static int ChunkTransfer(byte[] payload) => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(1));
}

public sealed class FsMessage
{
    public string Op { get; set; } = "";
    public int Id { get; set; }
    public int Transfer { get; set; }
    public string? Path { get; set; }
    public string? Path2 { get; set; }
    public bool Ok { get; set; }
    public string? Text { get; set; }
    public long Size { get; set; }
    public bool Overwrite { get; set; }
    public List<FsEntry>? Entries { get; set; }
}

public sealed class FsEntry
{
    public string Name { get; set; } = "";
    public bool Dir { get; set; }
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    /// <summary>Nur bei Laufwerken: Bezeichnung und Art, z. B. "Daten (Festplatte)".</summary>
    public string? Info { get; set; }
}

/// <summary>Gemeinsame Dateioperationen (Server für den Shack-PC, Client für die linke Seite).</summary>
public static class FsOps
{
    /// <summary>
    /// Laufwerke parallel abfragen, jedes höchstens 1,5 s: getrennte Netzlaufwerke oder leere Kartenleser
    /// können bei der Abfrage sonst viele Sekunden hängen.
    /// </summary>
    public static List<FsEntry> Drives()
    {
        var drives = DriveInfo.GetDrives();
        var tasks = drives.Select(d => Task.Run(() => Describe(d))).ToArray();
        var result = new List<FsEntry>();
        for (int i = 0; i < drives.Length; i++)
        {
            if (tasks[i].Wait(1500))
            {
                if (tasks[i].Result is { } e) result.Add(e);
            }
            else if (drives[i].DriveType == DriveType.Network)
            {
                result.Add(new FsEntry { Name = drives[i].Name, Dir = true, Info = "Netzlaufwerk – antwortet nicht" });
            }
        }
        return result;
    }

    private static FsEntry? Describe(DriveInfo d)
    {
        try
        {
            if (!d.IsReady) return null;
            return new FsEntry
            {
                Name = d.Name,
                Dir = true,
                Size = d.TotalFreeSpace,
                Info = $"{(string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel + " – ")}{DriveKind(d.DriveType)}, " +
                       $"{d.TotalFreeSpace / 1_000_000_000.0:F0} von {d.TotalSize / 1_000_000_000.0:F0} GB frei",
            };
        }
        catch
        {
            return null;
        }
    }

    private static string DriveKind(DriveType t) => t switch
    {
        DriveType.Fixed => "Festplatte",
        DriveType.Removable => "Wechseldatenträger",
        DriveType.Network => "Netzlaufwerk",
        DriveType.CDRom => "CD/DVD",
        _ => t.ToString(),
    };

    public static List<FsEntry> List(string path)
    {
        var dir = new DirectoryInfo(path);
        var list = new List<FsEntry>();
        foreach (var d in dir.EnumerateDirectories())
        {
            if ((d.Attributes & FileAttributes.System) != 0 && (d.Attributes & FileAttributes.Hidden) != 0) continue;
            list.Add(new FsEntry { Name = d.Name, Dir = true, Modified = d.LastWriteTime });
        }
        foreach (var f in dir.EnumerateFiles())
        {
            if ((f.Attributes & FileAttributes.System) != 0 && (f.Attributes & FileAttributes.Hidden) != 0) continue;
            list.Add(new FsEntry { Name = f.Name, Size = f.Length, Modified = f.LastWriteTime });
        }
        return list;
    }

    public static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    /// <summary>Umbenennen/Verschieben auf derselben Seite (auch über Laufwerksgrenzen).</summary>
    public static void Move(string from, string to)
    {
        if (Directory.Exists(from))
        {
            if (string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
                Directory.Move(from, to);
            else
            {
                CopyTree(from, to);
                Directory.Delete(from, true);
            }
        }
        else
        {
            File.Move(from, to);
        }
    }

    /// <summary>Kopieren auf derselben Seite (Ordner rekursiv).</summary>
    public static void Copy(string from, string to)
    {
        if (Directory.Exists(from)) CopyTree(from, to);
        else File.Copy(from, to, overwrite: false);
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: false);
        foreach (var d in Directory.GetDirectories(from))
            CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
