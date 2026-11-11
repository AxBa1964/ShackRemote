using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ShackRemote.Core;

namespace ShackRemote.UI;

/// <summary>
/// Eine Seite des Dateifensters: Kopf (Rechnername), Laufwerk, Pfadzeile, Liste mit Windows-Symbolen.
/// Leerer Pfad = Übersicht der Laufwerke.
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
    private readonly Label _info;
    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = SystemInformation.SmallIconSize };
    private List<FsEntry> _driveCache = new();
    private bool _loading;

    public event Action? PaneActivated;
    /// <summary>Ziel, Quellseite (null = Windows-Explorer), Pfade, Zielordner (null = aktueller Ordner), Kopieren statt Verschieben.</summary>
    public event Action<FilePane, FilePane?, List<string>, string?, bool>? ItemsDropped;
    public event Action<string>? Log;
    public event Action<string>? PathChanged;

    private sealed record DragPayload(FilePane Source, List<string> Paths);

    // Das gezogene Objekt bleibt im Prozess; im DataObject steht nur eine Markierung
    // (vermeidet Serialisierung eigener Typen beim Ziehen).
    private static DragPayload? _currentDrag;

    public FilePane(IFileSide side, string title, string subtitle)
    {
        Side = side;
        Title = title;
        BackColor = Theme.Window;

        // --- Kopf ----------------------------------------------------------------------------
        var head = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F),
            Margin = new Padding(4, 8, 4, 0),
        };
        var sub = new Label { Text = subtitle, AutoSize = true, ForeColor = Theme.SubtleText, Margin = new Padding(5, 0, 4, 6) };

        // --- Navigation -------------------------------------------------------------------------
        _drives = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 90,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 4, 4, 3),
        };
        _drives.SelectionChangeCommitted += async (_, _) =>
        {
            if (_drives.SelectedItem is FsEntry d) await LoadAsync(d.Name);
        };
        var up = Theme.IconButton(Theme.Up, "Eine Ebene höher (Rücktaste)");
        up.Click += async (_, _) => await UpAsync();
        var refresh = Theme.IconButton(Theme.Refresh, "Neu einlesen");
        refresh.Click += async (_, _) => await RefreshAsync();
        _path = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(4, 5, 4, 3) };
        _path.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await LoadAsync(_path.Text.Trim());
        };

        var nav = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nav.Controls.Add(up, 0, 0);
        nav.Controls.Add(_drives, 1, 0);
        nav.Controls.Add(_path, 2, 0);
        nav.Controls.Add(refresh, 3, 0);

        // --- Liste ------------------------------------------------------------------------------
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            AllowDrop = true,
            BorderStyle = BorderStyle.None,
            SmallImageList = _icons,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        Theme.ExplorerStyle(_list);
        _list.Columns.Add("Name", 300);
        _list.Columns.Add("Größe", 100, HorizontalAlignment.Right);
        _list.Columns.Add("Geändert", 150);
        _list.HandleCreated += (_, _) => SizeColumns();
        _list.Resize += (_, _) => SizeColumns();
        _list.DoubleClick += async (_, _) => await OpenSelectedAsync();
        _list.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.Handled = true; await OpenSelectedAsync(); }
            else if (e.KeyCode == Keys.Back) { e.Handled = true; await UpAsync(); }
            else if (e.Control && e.KeyCode == Keys.A) { e.Handled = true; foreach (ListViewItem i in _list.Items) i.Selected = i.Tag is FsEntry; }
        };
        _list.SelectedIndexChanged += (_, _) => UpdateInfo();
        _list.Enter += (_, _) => PaneActivated?.Invoke();
        _path.Enter += (_, _) => PaneActivated?.Invoke();
        _list.ItemDrag += OnItemDrag;
        _list.DragEnter += OnDragOver;
        _list.DragOver += OnDragOver;
        _list.DragDrop += OnDragDrop;

        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Theme.Border, Margin = new Padding(0) };
        frame.Controls.Add(_list);

        _info = new Label { AutoSize = true, ForeColor = Theme.SubtleText, Margin = new Padding(4, 4, 4, 2) };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10, 4, 10, 6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(head, 0, 0);
        layout.Controls.Add(sub, 0, 1);
        layout.Controls.Add(nav, 0, 2);
        layout.Controls.Add(frame, 0, 3);
        layout.Controls.Add(_info, 0, 4);
        Controls.Add(layout);
    }

    public void FocusList() => _list.Focus();

    /// <summary>Spaltenbreiten passend zur Skalierung; "Name" füllt den Rest.</summary>
    private void SizeColumns()
    {
        if (_list.Columns.Count < 3) return;
        int size = LogicalToDeviceUnits(90), date = LogicalToDeviceUnits(135);
        _list.Columns[1].Width = size;
        _list.Columns[2].Width = date;
        _list.Columns[0].Width = Math.Max(LogicalToDeviceUnits(150), _list.ClientSize.Width - size - date - 4);
    }

    private void UpdateInfo()
    {
        var sel = SelectedEntries;
        int dirs = sel.Count(e => e.Dir), files = sel.Count - dirs;
        long bytes = sel.Where(e => !e.Dir).Sum(e => e.Size);
        int total = _list.Items.Cast<ListViewItem>().Count(i => i.Tag is FsEntry);
        _info.Text = sel.Count == 0
            ? $"{total} Einträge"
            : $"{sel.Count} von {total} markiert" + (files > 0 ? $" – {files} Datei(en), {FormatSize(bytes)}" : "") + (dirs > 0 ? $", {dirs} Ordner" : "");
    }

    /// <summary>Markierte Einträge (ohne "..").</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<FsEntry> SelectedEntries =>
        InvokeRequired
            ? (List<FsEntry>)Invoke(new Func<List<FsEntry>>(() => SelectedEntries))
            : _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<FsEntry>().ToList();

    // ---- Navigation -------------------------------------------------------------------------

    /// <summary>Laufwerke einmal einlesen (mit Zeitmessung) und den Startordner öffnen.</summary>
    public async Task ShowDrivesAsync(string? startPath)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _driveWatch = sw;
            _driveCache = await Side.DrivesAsync(OnDriveUpdate);
            FillDriveBox();
            var pending = _driveCache.Where(d => d.Pending).Select(d => d.Name.TrimEnd('\\')).ToList();
            Log?.Invoke($"{Title}: {_driveCache.Count} Laufwerke in {sw.Elapsed.TotalSeconds:F1} s" +
                        (pending.Count > 0 && Side is RemoteSide ? " – langsam: " + string.Join(", ", pending) : ""));
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Title}: {ex.Message}");
        }
        if (!string.IsNullOrEmpty(startPath) && await TryLoadAsync(startPath)) return;
        await LoadAsync("");
    }

    private Stopwatch _driveWatch = Stopwatch.StartNew();

    private void FillDriveBox()
    {
        var selected = (_drives.SelectedItem as FsEntry)?.Name;
        _drives.Items.Clear();
        foreach (var d in _driveCache) _drives.Items.Add(d);
        _drives.DisplayMember = nameof(FsEntry.Name);
        SelectDriveFor(CurrentPath.Length > 0 ? CurrentPath : selected ?? "");
    }

    /// <summary>Nachgereichte Laufwerksdetails (aus einem Hintergrund-Thread).</summary>
    private void OnDriveUpdate(FsEntry old, FsEntry? updated)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => OnDriveUpdate(old, updated)); return; }

        int index = _driveCache.FindIndex(d => d.Name == old.Name);
        if (index < 0) return;
        if (updated is null) _driveCache.RemoveAt(index);
        else _driveCache[index] = updated;
        FillDriveBox();

        if (_driveWatch.Elapsed > TimeSpan.FromSeconds(1))
            Log?.Invoke($"{Title}: {old.Name.TrimEnd('\\')} " +
                        (updated is null ? "nicht bereit" : $"nach {_driveWatch.Elapsed.TotalSeconds:F1} s: {updated.Info}"));

        // Steht die Laufwerksübersicht gerade offen: Zeile anpassen
        if (CurrentPath.Length != 0) return;
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is not FsEntry e || e.Name != old.Name) continue;
            if (updated is null) { _list.Items.Remove(item); }
            else
            {
                item.Tag = updated;
                item.SubItems[2].Text = updated.Info ?? "";
            }
            break;
        }
        UpdateInfo();
    }

    /// <summary>Aktuellen Ordner neu einlesen; darf aus jedem Thread aufgerufen werden.</summary>
    public Task RefreshAsync() =>
        InvokeRequired ? (Task)Invoke(new Func<Task>(RefreshAsync)) : LoadAsync(CurrentPath, reloadDrives: CurrentPath.Length == 0);

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

    private async Task<bool> TryLoadAsync(string path)
    {
        try
        {
            await Side.ListAsync(path);
        }
        catch
        {
            return false;
        }
        await LoadAsync(path);
        return true;
    }

    private async Task LoadAsync(string path, bool reloadDrives = false)
    {
        if (_loading) return;
        _loading = true;
        UseWaitCursor = true;
        try
        {
            if (path.Length == 2 && path[1] == ':') path += "\\";   // "D:" -> "D:\"
            List<FsEntry> entries;
            if (path.Length == 0)
            {
                if (reloadDrives || _driveCache.Count == 0)
                {
                    _driveWatch = Stopwatch.StartNew();
                    _driveCache = await Side.DrivesAsync(OnDriveUpdate);
                    FillDriveBox();
                }
                entries = _driveCache;
            }
            else
            {
                entries = await Side.ListAsync(path);
            }
            CurrentPath = path;
            _path.Text = path.Length == 0 ? "Laufwerke" : path;
            SelectDriveFor(path);

            _list.BeginUpdate();
            _list.Items.Clear();
            if (path.Length > 0)
                _list.Items.Add(new ListViewItem(new[] { "..", "", "" }) { Tag = "..", ImageKey = IconKey("..", true, null) });
            foreach (var e in entries.OrderByDescending(e => e.Dir).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = path.Length == 0
                    ? new ListViewItem(new[] { e.Name, "", e.Info ?? "" }) { ImageKey = IconKey(e.Name, true, "drive") }
                    : new ListViewItem(new[]
                    {
                        e.Name,
                        e.Dir ? "" : FormatSize(e.Size),
                        e.Modified.ToString("dd.MM.yyyy HH:mm"),
                    }) { ImageKey = IconKey(e.Name, e.Dir, null) };
                item.Tag = e;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            UpdateInfo();
            if (path.Length > 0) PathChanged?.Invoke(path);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{Title}: {ex.Message}");
            _path.Text = CurrentPath.Length == 0 ? "Laufwerke" : CurrentPath;
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

    // ---- Windows-Symbole (wie im Explorer, nach Dateiendung) -------------------------------------------

    private string IconKey(string name, bool dir, string? special)
    {
        string key = special ?? (dir ? ":dir" : Path.GetExtension(name).ToLowerInvariant());
        if (key.Length == 0) key = ":file";
        if (_icons.Images.ContainsKey(key)) return key;
        var icon = special == "drive"
            ? ShellIcon(Environment.GetFolderPath(Environment.SpecialFolder.System)[..3], useAttributes: false, dir: true)
            : ShellIcon(dir ? "ordner" : "datei" + (key.StartsWith(':') ? "" : key), useAttributes: true, dir);
        if (icon is not null) _icons.Images.Add(key, icon);
        return key;
    }

    /// <summary>Großes Shell-Symbol einer Datei (z. B. explorer.exe) – wird von der Kopfleiste genutzt.</summary>
    internal static Icon? LargeShellIcon(string path)
    {
        const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
            return null;
        try { return (Icon)Icon.FromHandle(info.hIcon).Clone(); }
        finally { DestroyIcon(info.hIcon); }
    }

    private static Icon? ShellIcon(string path, bool useAttributes, bool dir)
    {
        const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
        const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | (useAttributes ? SHGFI_USEFILEATTRIBUTES : 0);
        if (SHGetFileInfo(path, dir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
            return null;
        try { return (Icon)Icon.FromHandle(info.hIcon).Clone(); }
        finally { DestroyIcon(info.hIcon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

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
