using System.Collections.ObjectModel;
using EflayGameSaveManager.Maui.Services;

namespace EflayGameSaveManager.Maui;

/// <summary>In-app browser and manual entry for paths hidden by the system picker.</summary>
public sealed class CustomPathPage : ContentPage
{
    private readonly TaskCompletionSource<StorageSelectionResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Entry _path = new() { Placeholder = "/data/data/包名/files", AutomationId = "CustomPathEntry" };
    private readonly Label _status = new() { FontSize = 12 };
    private readonly ObservableCollection<PathItem> _entries = [];
    private readonly CollectionView _items = new()
    {
        SelectionMode = SelectionMode.Single,
        ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
        BackgroundColor = Colors.White,
        EmptyView = new ContentView { Padding = 20, Content = new Label
            { Text = "此目录没有可选择的项目", FontSize = 13, TextColor = Color.FromArgb("#64748B") } }
    };
    private string _directoryPath = "/";
    private readonly bool _folder;
    private readonly bool _zip;
    private bool _loading;

    public Task<StorageSelectionResult> Result => _completion.Task;

    public CustomPathPage(string initialPath, bool folder, bool zip)
    {
        _folder = folder;
        _zip = zip;
        _items.ItemsSource = _entries;
        _items.ItemTemplate = new DataTemplate(CreateRow);
        _items.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is not PathItem item || _loading) return;
            _items.SelectedItem = null;
            var selected = Path.Combine(_directoryPath, item.Name);
            if (item.IsDirectory) await BrowseAsync(selected);
            else { _path.Text = selected; _status.Text = "已选中文件，点击“使用此路径”确认。"; }
        };
        Title = "自定义选择路径";
        BackgroundColor = Color.FromArgb("#FFF8EE");
        Padding = new Thickness(18, 28, 18, 18);
        _path.Text = initialPath.StartsWith('/') ? initialPath : "/data/data";
        var go = new Button { Text = "打开目录" };
        go.Clicked += async (_, _) => await BrowseAsync(_path.Text ?? "/data/data");
        var up = new Button { Text = "上一级" };
        up.Clicked += async (_, _) => await BrowseAsync(Path.GetDirectoryName((_path.Text ?? "/").TrimEnd('/')) ?? "/");
        var cancel = new Button { Text = "取消" };
        cancel.Clicked += async (_, _) => await FinishAsync(StorageSelectionResult.Cancelled("已取消自定义路径选择。"));
        var select = new Button { Text = "使用此路径", AutomationId = "UseCustomPath" };
        select.Clicked += async (_, _) =>
        {
            var path = _path.Text?.Trim() ?? "";
            if (!path.StartsWith('/')) { _status.Text = "请输入以 / 开头的绝对路径。"; return; }
            if (_zip && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { _status.Text = "ZIP 模式请选择或填写 .zip 文件路径。"; return; }
            await FinishAsync(StorageSelectionResult.Success(path, "已选择自定义路径。"));
        };
        var navigation = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 8 };
        navigation.Add(up); navigation.Add(go, 1);
        var actions = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)], ColumnSpacing = 8 };
        actions.Add(cancel); actions.Add(select, 1);
        var layout = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)], RowSpacing = 10 };
        layout.Add(new Label { Text = folder ? "选择文件夹，也可直接填写完整路径" : "选择文件，也可直接填写完整路径", FontSize = 18 }, 0, 0);
        layout.Add(_path, 0, 1); layout.Add(navigation, 0, 2); layout.Add(_status, 0, 3);
        layout.Add(new Border { Stroke = Color.FromArgb("#E2E8F0"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = _items }, 0, 4);
        layout.Add(actions, 0, 5);
        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var path = _path.Text ?? "/data/data";
        await BrowseAsync(!_folder && path != "/data/data" ? Path.GetDirectoryName(path) ?? "/data/data" : path);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _completion.TrySetResult(StorageSelectionResult.Cancelled("已取消自定义路径选择。"));
    }

    private async Task FinishAsync(StorageSelectionResult result)
    {
        if (!_completion.TrySetResult(result)) return;
        await Navigation.PopModalAsync();
    }

    private async Task BrowseAsync(string path)
    {
        if (_loading) return;
        _loading = true;
        _entries.Clear();
        _directoryPath = path;
        _path.Text = path;
        _status.Text = "正在读取目录…";
        try
        {
            var entries = await Task.Run(() => ReadDirectory(path));
            foreach (var entry in entries.Where(entry => entry.IsDirectory || !_folder && (!_zip || entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))))
                _entries.Add(entry);
            if (_entries.Count > 0) _items.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
            _status.Text = "可进入目录，或直接编辑上方路径后确认。";
        }
        catch (Exception ex) { _status.Text = ex.Message + "\n仍可手动填写路径；读取 /data/data 私有目录通常需要 root Shizuku。"; }
        finally { _loading = false; }
    }

    private static View CreateRow()
    {
        var row = new Grid
        {
            Padding = new Thickness(12, 10), ColumnSpacing = 10, RowSpacing = 4,
            ColumnDefinitions = [new ColumnDefinition(28), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(14)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(1)],
            BackgroundColor = Colors.White, MinimumHeightRequest = 66
        };
        var icon = new Label { FontSize = 22, VerticalOptions = LayoutOptions.Center };
        icon.SetBinding(Label.TextProperty, Binding.Create(static (PathItem item) => item.Icon));
        row.Add(icon); Grid.SetRowSpan(icon, 2);
        var name = new Label { FontSize = 14, TextColor = Color.FromArgb("#1E293B"),
            HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.TailTruncation };
        name.SetBinding(Label.TextProperty, Binding.Create(static (PathItem item) => item.Name));
        row.Add(name, 1);
        var kind = new Label { FontSize = 11, TextColor = Color.FromArgb("#64748B"), HorizontalTextAlignment = TextAlignment.Start };
        kind.SetBinding(Label.TextProperty, Binding.Create(static (PathItem item) => item.Kind));
        row.Add(kind, 1, 1);
        var count = new Label { FontSize = 12, TextColor = Color.FromArgb("#64748B"), VerticalOptions = LayoutOptions.Center };
        count.SetBinding(Label.TextProperty, Binding.Create(static (PathItem item) => item.CountText));
        row.Add(count, 2); Grid.SetRowSpan(count, 2);
        var arrow = new Label { Text = "›", FontSize = 22, TextColor = Color.FromArgb("#94A3B8"), VerticalOptions = LayoutOptions.Center };
        arrow.SetBinding(IsVisibleProperty, Binding.Create(static (PathItem item) => item.IsDirectory));
        row.Add(arrow, 3); Grid.SetRowSpan(arrow, 2);
        var divider = new BoxView { BackgroundColor = Color.FromArgb("#EEF2F6"), HeightRequest = 1, Margin = new Thickness(0, 5, 0, -10) };
        row.Add(divider, 0, 2); Grid.SetColumnSpan(divider, 4);
        return row;
    }

    private static IReadOnlyList<PathItem> ReadDirectory(string path)
    {
#if ANDROID
        // Direct enumeration can list a parent yet misidentify/count protected children.
        // Use the authorized elevated reader for restricted roots and their child counts.
        if (Platforms.Android.ShizukuFileService.IsRestrictedPath(path.TrimEnd('/') + "/") &&
            Platforms.Android.ShizukuInterop.IsAvailable && Platforms.Android.ShizukuInterop.HasPermission)
            return ReadElevatedDirectory(path);
#endif
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Select(item =>
            {
                var isDirectory = Directory.Exists(item);
                int? count = null;
                if (isDirectory)
                {
                    try { count = Directory.EnumerateFileSystemEntries(item).Count(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return new PathItem(Path.GetFileName(item), isDirectory, count);
            }).OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (UnauthorizedAccessException)
        {
#if ANDROID
            if (!Platforms.Android.ShizukuInterop.IsAvailable || !Platforms.Android.ShizukuInterop.HasPermission)
                throw new IOException(Platforms.Android.ShizukuFileService.StatusMessage);
            return ReadElevatedDirectory(path);
#else
            throw;
#endif
        }
    }

#if ANDROID
    private static IReadOnlyList<PathItem> ReadElevatedDirectory(string path)
    {
        var escaped = path.Replace("'", "'\\''", StringComparison.Ordinal);
        // Count immediate entries (including hidden entries), without descending into
        // children. One elevated process handles the whole listing, rather than one per row.
        var command = "test -d '" + escaped + "' && test -r '" + escaped + "' && test -x '" + escaped + "' || exit 1; " +
            "for p in '" + escaped + "'/* '" + escaped + "'/.[!.]* '" + escaped + "'/..?*; do [ -e \"$p\" ] || [ -L \"$p\" ] || continue; " +
            "if [ -d \"$p\" ]; then n=-1; if [ -r \"$p\" ] && [ -x \"$p\" ]; then n=0; " +
            "for c in \"$p\"/* \"$p\"/.[!.]* \"$p\"/..?*; do [ -e \"$c\" ] || [ -L \"$c\" ] || continue; n=$((n+1)); done; fi; " +
            "printf 'D\\t%s\\t%s\\n' \"$n\" \"${p##*/}\"; else printf 'F\\t-1\\t%s\\n' \"${p##*/}\"; fi; done";
        var (exit, stdout, stderr) = Platforms.Android.ShizukuInterop.RunShellCommand(command);
        if (exit != 0) throw new IOException("无法读取目录：" + stderr + " " + Platforms.Android.ShizukuFileService.StatusMessage);
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t', 3))
            .Where(fields => fields.Length == 3)
            .Select(fields => new PathItem(fields[2], fields[0] == "D", int.TryParse(fields[1], out var count) && count >= 0 ? count : null))
            .OrderByDescending(item => item.IsDirectory).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
#endif

    internal sealed record PathItem(string Name, bool IsDirectory, int? ChildCount)
    {
        public string Icon => IsDirectory ? "📁" : "📄";
        public string Kind => IsDirectory ? "文件夹" : Path.GetExtension(Name).Equals(".zip", StringComparison.OrdinalIgnoreCase) ? "ZIP 文件" : "文件";
        public string CountText => IsDirectory ? ChildCount is { } count ? $"{count} 项" : "无法读取" : "";
    }
}
