using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Classic.Avalonia.Theme;
using LibKwz;
using SixLabors.ImageSharp;

namespace KwzStudio;

/// <summary>A destination on the explicitly selected card; the path is never used as a UI label.</summary>
public sealed class SdFolderChoice : IDisposable
{
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Region => Directory.GetParent(Path)!.Name;
    public Bitmap? Icon { get; }

    public SdFolderChoice(string path)
    {
        Path = path;
        try
        {
            using var image = KwzFolderIcon.Load(System.IO.Path.Combine(path, "!.ico"));
            using var png = new MemoryStream(); image.SaveAsPng(png); png.Position = 0;
            Icon = new Bitmap(png);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.Cryptography.CryptographicException)
        { /* Missing or damaged icons must not prevent selecting an otherwise valid folder. */ }
    }

    public void Dispose() => Icon?.Dispose();
}

/// <summary>A fresh view of the selected SD root, including empty folders without !!.lst.</summary>
public sealed class SdFolderDialog : ClassicWindow
{
    private readonly string _root;
    private readonly ListBox _choices = new() { Name = "SdFolderChoices", MinHeight = 240,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _accept = new() { Name = "UseSdFolder", Padding = new Thickness(12, 6) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private SdFolderChoice[] _targets = [];
    private bool _refreshing, _initialized, _closed;

    public SdFolderDialog(string root)
    {
        _root = Path.GetFullPath(root);
        Title = L.T("Import to SD card"); Width = 650; Height = 440; MinWidth = 600; MinHeight = 440; FontSize = 12;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        body.Children.Add(new TextBlock { Text = L.T("Close the game before importing. Choose the destination folder. A new copy will be added; existing animations will be kept."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        _choices.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel());
        _choices.ItemTemplate = new FuncDataTemplate<SdFolderChoice>((folder, _) => FolderCard(folder!));
        ScrollViewer.SetHorizontalScrollBarVisibility(_choices, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        Grid.SetRow(_choices, 1); body.Children.Add(_choices);
        Grid.SetRow(_message, 2); _message.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(_message);
        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var refresh = new Button { Name = "RefreshSdFolders", Content = L.T("Refresh"), Padding = new Thickness(12, 6) };
        refresh.Click += (_, _) => RefreshFolders(); DockPanel.SetDock(refresh, Dock.Left); actions.Children.Add(refresh);
        _accept.Content = L.T("Import to SD card");
        _choices.SelectionChanged += (_, _) => _accept.IsEnabled = _choices.SelectedItem is SdFolderChoice;
        _accept.Click += (_, _) =>
        {
            string? requested = (_choices.SelectedItem as SdFolderChoice)?.Path;
            RefreshFolders();
            if (requested != null && (_choices.SelectedItem as SdFolderChoice)?.Path == requested) Close(requested);
        };
        var cancel = new Button { Content = L.T("Cancel"), Padding = new Thickness(12, 6) };
        cancel.Click += (_, _) => Close(null);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        right.Children.Add(_accept); right.Children.Add(cancel); DockPanel.SetDock(right, Dock.Right); actions.Children.Add(right);
        Grid.SetRow(actions, 3); body.Children.Add(actions); Content = body;
        Opened += (_, _) => RefreshFolders();
        Activated += (_, _) => RefreshFolders();
        Closed += (_, _) => { _closed = true; _choices.ItemsSource = null; DisposeTargets(); };
        RefreshFolders();
    }

    private static Control FolderCard(SdFolderChoice folder)
    {
        var card = new StackPanel { Width = 128, Margin = new Thickness(4, 8), Spacing = 6 };
        Control icon;
        if (folder.Icon != null)
        {
            icon = new Avalonia.Controls.Image { Source = folder.Icon, Width = 72, Height = 72 };
            RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.None);
        }
        else
            icon = new Viewbox { Width = 72, Height = 72, Child = new Avalonia.Controls.Shapes.Path {
                Data = Geometry.Parse("M1,5 L1,21 L23,21 L23,7 L11,7 L9,3 L1,3 Z"),
                Fill = Brushes.Goldenrod, Stroke = Brushes.SaddleBrown, StrokeThickness = 1, Width = 24, Height = 24 } };
        card.Children.Add(icon);
        var name = new TextBlock { Text = folder.Name, TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 128 };
        ToolTip.SetTip(name, folder.Name); card.Children.Add(name);
        card.Children.Add(new TextBlock { Text = folder.Region, TextAlignment = TextAlignment.Center, FontSize = 11, Opacity = .65,
            FlowDirection = FlowDirection.LeftToRight });
        return card;
    }

    public void RefreshFolders()
    {
        if (_refreshing || _closed) return;
        _refreshing = true;
        try
        {
            string? previous = (_choices.SelectedItem as SdFolderChoice)?.Path;
            string[] paths;
            try
            {
                paths = KwzSdCard.FindFolders(_root).ToArray();
                _message.Text = L.T("No animation folders were found. Create a folder in Flipnote Studio 3D on this card first.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                paths = []; _message.Text = error.Message;
            }
            // Reload the icons too: the game can change !.ico without changing folder names.
            var next = paths.Select(path => new SdFolderChoice(path)).ToArray();
            _choices.ItemsSource = next; DisposeTargets(); _targets = next;
            int index = Array.FindIndex(next, folder => folder.Path == previous);
            // Never silently switch an already chosen destination when it disappears.
            _choices.SelectedIndex = index >= 0 ? index : !_initialized && next.Length > 0 ? 0 : -1;
            _initialized = true;
            _accept.IsEnabled = _choices.SelectedItem is SdFolderChoice;
            _message.IsVisible = next.Length == 0;
        }
        finally { _refreshing = false; }
    }

    private void DisposeTargets() { foreach (var folder in _targets) folder.Dispose(); _targets = []; }
}
