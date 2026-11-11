using System.ComponentModel;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Eine Seite des Dateifensters: Laufwerksauswahl, Pfadzeile, Liste (Name, Größe, Geändert).
/// Leerer Pfad = Übersicht der Laufwerke ("Dieser PC").
/// </summary>
public sealed class FilePane : UserControl
{
    private const string InternalFormat = "ShackRemote.FilePaneItems";

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IFileSide Side { get; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CurrentPath { get; private set; } = "";

    private readonly ComboBox _drives;
    private readonly TextBox _path;
    private readonly ListView _list;
    private readonly Label _title;
    private bool _loading;

    public event Action? PaneActivated;
    /// <summary>Ziel, Quellseite (null = Windows-Explorer), Pfade, Zielordner (null = aktueller Ordner), Kopieren statt Verschieben.</summary>
    public event Action<FilePane, FilePane?, List<string>, string?, bool>? ItemsDropped;
    public event Action<string>? Log;

    private sealed record DragPayload(FilePane Source, List<string> Paths);

    // Das gezogene Objekt bleibt im Prozess; im DataObject steht nur eine Markierung
    // (vermeidet Serialisierung eigener Typen beim Ziehen).
    private static DragPayload? _currentDrag;

    public FilePane(IFileSide side, string title)
    {
        Side = side;
        Title = title;
        Font = new Font("Segoe UI", 10F);

        _title = LayoutKit.MakeLabel(title, bold: true);

        _drives = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Margin = new Padding(3, 4, 3, 3) };
        _drives.SelectionChangeCommitted += async (_, _) =>
        {
            if (_drives.SelectedItem is FsEntry d) await LoadAsync(d.Name);
        };
        var up = LayoutKit.MakeButton("▲");
        up.Click += async (_, _) => await UpAsync();
        new ToolTip().SetToolTip(up, "Eine Ebene höher (Rücktaste)");
        var refresh = LayoutKit.MakeButton("⟳");
        refresh.Click += async (_, _) => await RefreshAsync();
        new ToolTip().SetToolTip(refresh, "Neu einlesen");
        _path = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) };
        _path.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await LoadAsync(_path.Text.Trim());
        };

        var nav = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Margin = new Padding(0) };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.Controls.Add(_drives, 0, 0);
        nav.Controls.Add(up, 1, 0);
        nav.Controls.Add(_path, 2, 0);
        nav.Controls.Add(refresh, 3, 0);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            AllowDrop = true,
        };
        _list.Columns.Add("Name", 320);
        _list.Columns.Add("Größe", 110, HorizontalAlignment.Right);
        _list.Columns.Add("Geändert", 150);
        _list.DoubleClick += async (_, _) => await OpenSelectedAsync();
        _list.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; await OpenSelectedAsync(); }
            else if (e.KeyCode == Keys.Back) { e.Handled = true; await UpAsync(); }
            else if (e.Control && e.KeyCode == Keys.A) { e.Handled = true; foreach (ListViewItem i in _list.Items) i.Selected = i.Tag is FsEntry; }
        };
        _list.Enter += (_, _) => PaneActivated?.Invoke();
        _path.Enter += (_, _) => PaneActivated?.Invoke();
        _list.ItemDrag += OnItemDrag;
        _list.DragEnter += OnDragOver;
        _list.DragOver += OnDragOver;
        _list.DragDrop += OnDragDrop;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(_title, 0, 0);
        layout.Controls.Add(nav, 0, 1);
        layout.Controls.Add(_list, 0, 2);
        Controls.Add(layout);
    }

    public void FocusList() => _list.Focus();

    /// <summary>Markierte Einträge (ohne "..").</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<FsEntry> SelectedEntries =>
        InvokeRequired
            ? (List<FsEntry>)Invoke(new Func<List<FsEntry>>(() => SelectedEntries))
            : _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<FsEntry>().ToList();

    // ---- Navigation -------------------------------------------------------------------------

    public async Task ShowDrivesAsync()
    {
        try
        {
            var drives = await Side.DrivesAsync();
            _drives.Items.Clear();
            foreach (var d in drives) _drives.Items.Add(d);
            _drives.DisplayMember = nameof(FsEntry.Name);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Title}: {ex.Message}");
        }
        await LoadAsync("");
    }

    /// <summary>Aktuellen Ordner neu einlesen; darf aus jedem Thread aufgerufen werden.</summary>
    public Task RefreshAsync() =>
        InvokeRequired ? (Task)Invoke(new Func<Task>(RefreshAsync)) : LoadAsync(CurrentPath);

    private async Task UpAsync()
    {
        if (CurrentPath.Length == 0) return;
        var parent = Path.GetDirectoryName(CurrentPath.TrimEnd('\\'));
        // "C:\" hat keinen übergeordneten Ordner -> Laufwerksübersicht
        await LoadAsync(CurrentPath.TrimEnd('\\').Length <= 2 ? "" : parent ?? "");
    }

    private async Task OpenSelectedAsync()
    {
        if (_list.SelectedItems.Count != 1) return;
        var tag = _list.SelectedItems[0].Tag;
        if (tag is string s && s == "..") { await UpAsync(); return; }
        if (tag is FsEntry e && e.Dir)
            await LoadAsync(CurrentPath.Length == 0 ? e.Name : Path.Combine(CurrentPath, e.Name));
    }

    private async Task LoadAsync(string path)
    {
        if (_loading) return;
        _loading = true;
        UseWaitCursor = true;
        try
        {
            if (path.Length == 2 && path[1] == ':') path += "\\";   // "D:" -> "D:\"
            List<FsEntry> entries = path.Length == 0 ? await Side.DrivesAsync() : await Side.ListAsync(path);
            CurrentPath = path;
            _path.Text = path.Length == 0 ? "(Laufwerke)" : path;
            SelectDriveFor(path);

            _list.BeginUpdate();
            _list.Items.Clear();
            if (path.Length > 0)
                _list.Items.Add(new ListViewItem(new[] { "📁 ..", "", "" }) { Tag = ".." });
            foreach (var e in entries.OrderByDescending(e => e.Dir).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = path.Length == 0
                    ? new ListViewItem(new[] { "💽 " + e.Name, "", e.Info ?? "" })
                    : new ListViewItem(new[]
                    {
                        (e.Dir ? "📁 " : "📄 ") + e.Name,
                        e.Dir ? "" : FormatSize(e.Size),
                        e.Modified.ToString("dd.MM.yyyy HH:mm"),
                    });
                item.Tag = e;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Title}: {ex.Message}");
            _path.Text = CurrentPath.Length == 0 ? "(Laufwerke)" : CurrentPath;
        }
        finally
        {
            _loading = false;
            UseWaitCursor = false;
        }
    }

    private void SelectDriveFor(string path)
    {
        var root = path.Length == 0 ? null : Path.GetPathRoot(path);
        foreach (var item in _drives.Items)
            if (item is FsEntry d && string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase))
            {
                _drives.SelectedItem = d;
                return;
            }
        _drives.SelectedIndex = -1;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
    };

    // ---- Ziehen und Ablegen ------------------------------------------------------------------------

    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (CurrentPath.Length == 0) return;
        var paths = SelectedEntries.Select(x => Path.Combine(CurrentPath, x.Name)).ToList();
        if (paths.Count == 0) return;
        var data = new DataObject();
        data.SetData(InternalFormat, "1");
        _currentDrag = new DragPayload(this, paths);
        // Vom Laptop aus zusätzlich als normale Dateiliste: so kann man auch in den Windows-Explorer ziehen
        if (Side is LocalSide) data.SetData(DataFormats.FileDrop, paths.ToArray());
        try { _list.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move); }
        finally { _currentDrag = null; }
    }

    private (DragPayload? Internal, string[]? Files) Read(IDataObject? data) =>
        (data?.GetDataPresent(InternalFormat) == true ? _currentDrag : null,
         data?.GetDataPresent(DataFormats.FileDrop) == true ? data.GetData(DataFormats.FileDrop) as string[] : null);

    /// <summary>Ordner unter dem Mauszeiger (null = kein Ordner).</summary>
    private string? FolderAt(int screenX, int screenY)
    {
        var pt = _list.PointToClient(new Point(screenX, screenY));
        var item = _list.GetItemAt(pt.X, pt.Y);
        if (item?.Tag is FsEntry e && e.Dir && CurrentPath.Length > 0) return Path.Combine(CurrentPath, e.Name);
        return null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var (payload, files) = Read(e.Data);
        bool ctrl = (e.KeyState & 8) != 0;
        if (CurrentPath.Length == 0) { e.Effect = DragDropEffects.None; return; }
        if (payload is not null)
        {
            if (payload.Source != this) e.Effect = DragDropEffects.Copy;
            else e.Effect = FolderAt(e.X, e.Y) is null ? DragDropEffects.None : ctrl ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else e.Effect = files is not null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var (payload, files) = Read(e.Data);
        var folder = FolderAt(e.X, e.Y);
        bool ctrl = (e.KeyState & 8) != 0;
        if (payload is not null)
        {
            if (payload.Source == this && folder is null) return;
            ItemsDropped?.Invoke(this, payload.Source, payload.Paths, folder, ctrl || payload.Source != this);
        }
        else if (files is not null)
        {
            ItemsDropped?.Invoke(this, null, files.ToList(), folder, true);
        }
    }
}
