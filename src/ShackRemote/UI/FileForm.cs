using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Dateifenster wie ein Zwei-Spalten-Dateimanager: links der Laptop, rechts der Shack-PC.
/// Tasten: F5 Kopieren zur anderen Seite, F6 Verschieben, F7 Neuer Ordner, F2 Umbenennen, Entf Löschen,
/// Rücktaste eine Ebene höher, Enter/Doppelklick öffnet Ordner. Ziehen zwischen den Seiten kopiert,
/// Ziehen auf einen Ordner derselben Seite verschiebt, Ziehen aus dem Windows-Explorer lädt hoch bzw. kopiert.
/// </summary>
public sealed class FileForm : Form
{
    private readonly FilePane _local;
    private readonly FilePane _remote;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private readonly List<Button> _actionButtons = new();
    private FilePane _active;
    private CancellationTokenSource? _cts;

    public FileForm(SessionClient session, RemoteFs remoteFs)
    {
        LayoutKit.PrepareForm(this, $"ShackRemote – Dateien ({session.ServerName})");
        ClientSize = new Size(1200, 720);
        MinimumSize = new Size(800, 480);
        KeyPreview = true;

        _local = new FilePane(new LocalSide(), $"Laptop ({Environment.MachineName})");
        _remote = new FilePane(new RemoteSide(remoteFs), $"Shack-PC ({session.ServerName})");
        _active = _local;
        foreach (var p in new[] { _local, _remote })
        {
            p.PaneActivated += () => _active = p;
            p.ItemsDropped += OnDrop;
            p.Log += msg => SetStatus(msg);
        }

        // --- Knöpfe ------------------------------------------------------------------------
        Button B(string text, string tip, Func<Task> action)
        {
            var b = LayoutKit.MakeButton(text);
            b.Click += async (_, _) => await action();
            new ToolTip().SetToolTip(b, tip);
            _actionButtons.Add(b);
            return b;
        }
        var bar = LayoutKit.Row(
            B("Kopieren →  (F5)", "Markierte Einträge auf die andere Seite kopieren", () => TransferAsync(move: false)),
            B("Verschieben → (F6)", "Markierte Einträge auf die andere Seite verschieben", () => TransferAsync(move: true)),
            B("Neuer Ordner (F7)", "Ordner auf der aktiven Seite anlegen", NewFolderAsync),
            B("Umbenennen (F2)", "Markierten Eintrag umbenennen", RenameAsync),
            B("Löschen (Entf)", "Markierte Einträge endgültig löschen", DeleteAsync),
            B("Aktualisieren", "Beide Seiten neu einlesen", async () => { await _local.RefreshAsync(); await _remote.RefreshAsync(); }));

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_local);
        split.Panel2.Controls.Add(_remote);
        _local.Dock = DockStyle.Fill;
        _remote.Dock = DockStyle.Fill;

        _status = LayoutKit.MakeLabel("Bereit.");
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _progress = new ProgressBar { Width = 260, Height = 20, Margin = new Padding(6, 6, 6, 3), Visible = false };
        _cancel = LayoutKit.MakeButton("Abbrechen");
        _cancel.Visible = false;
        _cancel.Click += (_, _) => _cts?.Cancel();
        var statusRow = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 3, AutoSize = true, Padding = new Padding(4) };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.Controls.Add(_status, 0, 0);
        statusRow.Controls.Add(_progress, 1, 0);
        statusRow.Controls.Add(_cancel, 2, 0);

        bar.Dock = DockStyle.Top;
        Controls.Add(split);
        Controls.Add(bar);
        Controls.Add(statusRow);
        ResumeLayout(true);

        Shown += async (_, _) =>
        {
            split.SplitterDistance = split.Width / 2;
            await _local.ShowDrivesAsync();
            await _remote.ShowDrivesAsync();
            _local.FocusList();
        };

        KeyDown += async (_, e) =>
        {
            if (_cts is not null || e.Control || e.Alt) return;
            Func<Task>? action = e.KeyCode switch
            {
                Keys.F5 => () => TransferAsync(false),
                Keys.F6 => () => TransferAsync(true),
                Keys.F7 => NewFolderAsync,
                Keys.F2 => RenameAsync,
                Keys.Delete => DeleteAsync,
                _ => null,
            };
            if (action is null) return;
            if (ActiveControl is TextBox) return;   // in der Pfadzeile normal tippen
            e.Handled = true;
            await action();
        };

        FormClosing += (_, e) =>
        {
            if (_cts is null) return;
            if (MessageBox.Show(this, "Eine Übertragung läuft noch. Abbrechen und schließen?", "ShackRemote",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _cts.Cancel();
            else
                e.Cancel = true;
        };
    }

    private FilePane Other(FilePane p) => p == _local ? _remote : _local;

    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
        _status.Text = text;
    }

    // ---- Aktionen auf einer Seite -----------------------------------------------------------------

    private async Task NewFolderAsync()
    {
        var pane = _active;
        if (pane.CurrentPath.Length == 0) { SetStatus("Erst ein Laufwerk öffnen."); return; }
        var name = InputDialog.Ask(this, "Neuer Ordner", "Name des neuen Ordners:", "Neuer Ordner");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunAsync($"Lege Ordner „{name}“ an …", async _ =>
        {
            await pane.Side.MkdirAsync(Path.Combine(pane.CurrentPath, name.Trim()));
            await pane.RefreshAsync();
        });
    }

    private async Task RenameAsync()
    {
        var pane = _active;
        var entry = pane.SelectedEntries.FirstOrDefault();
        if (entry is null || pane.CurrentPath.Length == 0) return;
        var name = InputDialog.Ask(this, "Umbenennen", $"Neuer Name für „{entry.Name}“:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name) return;
        await RunAsync($"Benenne um …", async _ =>
        {
            await pane.Side.RenameAsync(Path.Combine(pane.CurrentPath, entry.Name), Path.Combine(pane.CurrentPath, name.Trim()));
            await pane.RefreshAsync();
        });
    }

    private async Task DeleteAsync()
    {
        var pane = _active;
        var entries = pane.SelectedEntries;
        if (entries.Count == 0 || pane.CurrentPath.Length == 0) return;
        var what = entries.Count == 1 ? $"„{entries[0].Name}“" : $"{entries.Count} Einträge";
        if (MessageBox.Show(this, $"{what} auf „{pane.Title}“ ENDGÜLTIG löschen (ohne Papierkorb)?\nOrdner werden samt Inhalt gelöscht.",
                "Löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        await RunAsync($"Lösche {what} …", async ct =>
        {
            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                await pane.Side.DeleteAsync(Path.Combine(pane.CurrentPath, e.Name));
            }
            await pane.RefreshAsync();
        });
    }

    // ---- Kopieren/Verschieben zwischen den Seiten ------------------------------------------------------

    private Task TransferAsync(bool move)
    {
        var from = _active;
        var names = from.SelectedEntries.Select(e => Path.Combine(from.CurrentPath, e.Name)).ToList();
        return TransferAsync(from, names, Other(from), Other(from).CurrentPath, move);
    }

    private async Task TransferAsync(FilePane from, List<string> sources, FilePane to, string targetFolder, bool move)
    {
        if (sources.Count == 0) return;
        if (targetFolder.Length == 0) { SetStatus("Auf der Zielseite erst ein Laufwerk/einen Ordner öffnen."); return; }
        bool? overwriteAll = null;
        await RunAsync(move ? "Verschiebe …" : "Kopiere …", async ct =>
        {
            // 1. Plan: alle Dateien und Ordner samt Größe
            SetStatus("Ermittle Dateien …");
            var plan = new List<(string Src, string Dst, bool Dir, long Size)>();
            foreach (var src in sources)
                await PlanAsync(from.Side, src, Path.Combine(targetFolder, Path.GetFileName(src.TrimEnd('\\'))), plan, ct);
            long total = Math.Max(1, plan.Sum(p => p.Size));
            long done = 0;
            int files = plan.Count(p => !p.Dir), fileNo = 0;

            // 2. Ausführen
            foreach (var item in plan)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Dir)
                {
                    try { await to.Side.MkdirAsync(item.Dst); } catch { /* existiert schon */ }
                    continue;
                }
                fileNo++;
                long before = done;
                void Progress(long bytes) => BeginInvoke(() =>
                {
                    _progress.Value = (int)Math.Min(1000, (before + bytes) * 1000 / total);
                    SetStatus($"{Path.GetFileName(item.Src)}  ({fileNo} von {files})  {FilePane.FormatSize(before + bytes)} von {FilePane.FormatSize(total)}");
                });

                bool overwrite = overwriteAll == true;
                while (true)
                {
                    try
                    {
                        await CopyFileAcrossAsync(from.Side, item.Src, to.Side, item.Dst, overwrite, Progress, ct);
                        break;
                    }
                    catch (IOException ex) when (ex.Message == "EXISTS" && !overwrite)
                    {
                        if (overwriteAll == false) goto skip;
                        var answer = AskOverwrite(item.Dst);
                        if (answer == DialogResult.Cancel) throw new OperationCanceledException();
                        if (answer == DialogResult.No) goto skip;
                        overwrite = true;
                        if (answer == DialogResult.Retry) overwriteAll = true;   // "Ja, alle"
                    }
                }
                done += item.Size;
                continue;
            skip:
                done += item.Size;
            }

            // 3. Verschieben: Quellen erst löschen, wenn alles kopiert ist
            if (move)
                foreach (var src in sources)
                    await from.Side.DeleteAsync(src);

            await from.RefreshAsync();
            await to.RefreshAsync();
        });
    }

    private static async Task PlanAsync(IFileSide side, string src, string dst, List<(string, string, bool, long)> plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var parent = Path.GetDirectoryName(src.TrimEnd('\\')) ?? src;
        var name = Path.GetFileName(src.TrimEnd('\\'));
        var entry = (await side.ListAsync(parent)).FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null) throw new IOException($"„{src}“ nicht gefunden.");
        if (!entry.Dir)
        {
            plan.Add((src, dst, false, entry.Size));
            return;
        }
        plan.Add((src, dst, true, 0));
        foreach (var child in await side.ListAsync(src))
        {
            var childSrc = Path.Combine(src, child.Name);
            var childDst = Path.Combine(dst, child.Name);
            if (child.Dir) await PlanAsync(side, childSrc, childDst, plan, ct);
            else plan.Add((childSrc, childDst, false, child.Size));
        }
    }

    /// <summary>Eine Datei von einer Seite zur anderen (oder auf derselben Seite kopieren).</summary>
    private static async Task CopyFileAcrossAsync(IFileSide from, string src, IFileSide to, string dst, bool overwrite,
        Action<long> progress, CancellationToken ct)
    {
        if (from is LocalSide && to is RemoteSide r)
            await r.Fs.UploadAsync(src, dst, overwrite, progress, ct);
        else if (from is RemoteSide rr && to is LocalSide)
        {
            if (!overwrite && (File.Exists(dst) || Directory.Exists(dst))) throw new IOException("EXISTS");
            await rr.Fs.DownloadAsync(src, dst, overwrite, progress, ct);
        }
        else
        {
            if (!overwrite && from is LocalSide && (File.Exists(dst) || Directory.Exists(dst))) throw new IOException("EXISTS");
            if (overwrite) { try { await to.DeleteAsync(dst); } catch { } }
            await to.CopyAsync(src, dst);
        }
    }

    /// <summary>Aus dem Arbeits-Thread: Rückfrage im UI-Thread.
    /// Ja = diese überschreiben, Wiederholen = alle überschreiben, Nein = überspringen, Abbrechen = alles abbrechen.</summary>
    private DialogResult AskOverwrite(string path) => (DialogResult)Invoke(new Func<DialogResult>(() =>
    {
        using var dlg = new Form
        {
            Text = "Datei existiert",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = Font,
        };
        var label = LayoutKit.MakeLabel($"„{path}“ existiert bereits. Überschreiben?");
        label.MaximumSize = new Size(560, 0);
        Button Btn(string t, DialogResult r) { var b = LayoutKit.MakeButton(t); b.DialogResult = r; return b; }
        var row = LayoutKit.Row(Btn("Ja", DialogResult.Yes), Btn("Ja, alle", DialogResult.Retry), Btn("Nein", DialogResult.No), Btn("Abbrechen", DialogResult.Cancel));
        var col = LayoutKit.Column(2);
        col.Dock = DockStyle.None;
        col.AutoSize = true;
        col.Controls.Add(label, 0, 0);
        col.Controls.Add(row, 0, 1);
        dlg.Controls.Add(col);
        return dlg.ShowDialog(this);
    }));

    // ---- Ziehen und Ablegen -------------------------------------------------------------------

    private async void OnDrop(FilePane target, FilePane? sourcePane, List<string> paths, string? targetFolder, bool copy)
    {
        var folder = targetFolder ?? target.CurrentPath;
        if (sourcePane is null)
        {
            // Aus dem Windows-Explorer: immer kopieren (auf den Laptop = lokal kopieren, auf den Shack-PC = hochladen)
            await TransferAsync(_local, paths, target, folder, move: false);
        }
        else if (sourcePane != target)
        {
            await TransferAsync(sourcePane, paths, target, folder, move: false);
        }
        else if (targetFolder is not null)
        {
            // Auf einen Ordner derselben Seite: verschieben (Strg gedrückt: kopieren)
            await RunAsync(copy ? "Kopiere …" : "Verschiebe …", async _ =>
            {
                foreach (var p in paths)
                {
                    var dst = Path.Combine(targetFolder, Path.GetFileName(p.TrimEnd('\\')));
                    if (string.Equals(p, dst, StringComparison.OrdinalIgnoreCase)) continue;
                    if (copy) await target.Side.CopyAsync(p, dst);
                    else await target.Side.RenameAsync(p, dst);
                }
                await target.RefreshAsync();
            });
        }
    }

    // ---- Ausführung mit Fortschritt und Abbruch ----------------------------------------------------------

    private async Task RunAsync(string title, Func<CancellationToken, Task> work)
    {
        if (_cts is not null) { SetStatus("Bitte warten – es läuft noch eine Aktion."); return; }
        _cts = new CancellationTokenSource();
        foreach (var b in _actionButtons) b.Enabled = false;
        _progress.Value = 0;
        _progress.Maximum = 1000;
        _progress.Visible = _cancel.Visible = true;
        SetStatus(title);
        try
        {
            await Task.Run(() => work(_cts.Token));
            SetStatus("Fertig.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Abgebrochen.");
        }
        catch (Exception ex)
        {
            SetStatus($"Fehler: {ex.Message}");
            MessageBox.Show(this, ex.Message, "ShackRemote – Dateien", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            foreach (var b in _actionButtons) b.Enabled = true;
            _progress.Visible = _cancel.Visible = false;
        }
    }
}

// =================================================================================================
// Seiten: Laptop (lokal) und Shack-PC (über RemoteFs)
// =================================================================================================

public interface IFileSide
{
    Task<List<FsEntry>> DrivesAsync();
    Task<List<FsEntry>> ListAsync(string path);
    Task MkdirAsync(string path);
    Task DeleteAsync(string path);
    Task RenameAsync(string from, string to);
    Task CopyAsync(string from, string to);
}

public sealed class LocalSide : IFileSide
{
    public Task<List<FsEntry>> DrivesAsync() => Task.Run(() => FsOps.Drives());
    public Task<List<FsEntry>> ListAsync(string path) => Task.Run(() => FsOps.List(path));
    public Task MkdirAsync(string path) => Task.Run(() => Directory.CreateDirectory(path));
    public Task DeleteAsync(string path) => Task.Run(() => FsOps.Delete(path));
    public Task RenameAsync(string from, string to) => Task.Run(() => FsOps.Move(from, to));
    public Task CopyAsync(string from, string to) => Task.Run(() => FsOps.Copy(from, to));
}

public sealed class RemoteSide : IFileSide
{
    public RemoteFs Fs { get; }
    public RemoteSide(RemoteFs fs) => Fs = fs;
    public Task<List<FsEntry>> DrivesAsync() => Fs.DrivesAsync();
    public Task<List<FsEntry>> ListAsync(string path) => Fs.ListAsync(path);
    public Task MkdirAsync(string path) => Fs.MkdirAsync(path);
    public Task DeleteAsync(string path) => Fs.DeleteAsync(path);
    public Task RenameAsync(string from, string to) => Fs.RenameAsync(from, to);
    public Task CopyAsync(string from, string to) => Fs.CopyAsync(from, to);
}
