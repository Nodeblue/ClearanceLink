using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace XPConnect.Host.Panel;

/// <summary>
/// The FMS-style panel: a 24-column screen with six line select keys each side and page keys below. Keys 1-6 press
/// the left keys, Shift+1-6 the right ones, F1-F4 change page; clicking either half of a row presses its key too.
/// </summary>
public sealed class PanelWindow : Window
{
    public static readonly FontFamily CduFont = new("avares://XPConnect/Panel/Fonts#B612 Mono");

    private const double DataSize = 19;
    private const double LabelSize = 13.5;
    private static readonly TimeSpan FlashTime = TimeSpan.FromSeconds(4);

    private static readonly IBrush Bezel = Brush("#2A2F33");
    private static readonly IBrush Screen = Brush("#05080A");
    private static readonly IBrush KeyFace = Brush("#454C52");
    private static readonly IBrush KeyText = Brush("#E8ECEF");

    private readonly PanelController? _controller;
    private readonly Func<PanelState> _state;
    private readonly TextBlock _title = Text(DataSize);
    private readonly TextBlock _scratch = Text(DataSize);
    private readonly TextBlock[] _labelLeft = new TextBlock[6];
    private readonly TextBlock[] _labelRight = new TextBlock[6];
    private readonly TextBlock[] _dataLeft = new TextBlock[6];
    private readonly TextBlock[] _dataRight = new TextBlock[6];
    private readonly Button[] _lskLeft = new Button[6];
    private readonly Button[] _lskRight = new Button[6];
    private readonly Dictionary<PanelPage, Button> _pageKeys = [];
    private readonly DispatcherTimer _timer;
    private CduPage? _shown;
    private string? _flash;
    private DateTime _flashUntil;
    private LiveLogWindow? _liveLog;

    public PanelWindow(PanelController controller)
        : this(controller.State)
    {
        _controller = controller;
    }

    /// <summary>For tests: a window over a fixed state, with no server behind it.</summary>
    public PanelWindow(Func<PanelState> state)
    {
        _state = state;
        Title = "XPConnect";
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Bezel;
        Content = Build();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Render());
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _liveLog?.Close();
        };
        KeyDown += OnKeyDown;
        Render();
    }

    public PanelPage Page { get; private set; } = PanelPage.Status;

    /// <summary>The page as last drawn (tests read it).</summary>
    public CduPage? Shown => _shown;

    public void ShowPage(PanelPage page)
    {
        Page = page;
        _flash = null;
        Render();
    }

    public void Render()
    {
        var flash = DateTime.Now < _flashUntil ? _flash : null;
        var page = CduPages.Build(Page, _state(), flash);
        _shown = page;
        _title.Text = page.Title;
        for (var i = 0; i < 6; i++)
        {
            var row = page.Rows[i];
            _labelLeft[i].Text = row.LabelLeft;
            _labelRight[i].Text = row.LabelRight;
            Paint(_dataLeft[i], row.Left);
            Paint(_dataRight[i], row.Right);
            _lskLeft[i].IsEnabled = row.KeyLeft != CduKey.None;
            _lskRight[i].IsEnabled = row.KeyRight != CduKey.None;
        }

        Paint(_scratch, page.Scratchpad);
        foreach (var (key, button) in _pageKeys)
        {
            button.Foreground = key == Page ? Colour(CduColor.Green) : KeyText;
        }
    }

    public async Task PressAsync(int row, bool right)
    {
        if (_shown is null)
        {
            return;
        }

        var key = right ? _shown.Rows[row].KeyRight : _shown.Rows[row].KeyLeft;
        switch (key)
        {
            case CduKey.None:
                return;
            case CduKey.GoStatus:
                ShowPage(PanelPage.Status);
                return;
            case CduKey.GoSetup:
                ShowPage(PanelPage.Setup);
                return;
            case CduKey.GoSupport:
                ShowPage(PanelPage.Support);
                return;
            case CduKey.GoAbout:
                ShowPage(PanelPage.About);
                return;
            case CduKey.ChooseBatcFolder:
            case CduKey.ChooseXPlaneFolder:
                await ChooseFolderAsync(key);
                return;
            case CduKey.ShowLiveLog:
                ShowLiveLog();
                return;
        }

        if (_controller is not null)
        {
            Flash("WORKING...");
            Flash(await _controller.DoAsync(key));
        }
    }

    private async Task ChooseFolderAsync(CduKey key)
    {
        if (_controller is null)
        {
            return;
        }

        var batc = key == CduKey.ChooseBatcFolder;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = batc ? "Choose the BeyondATC folder (the one with BeyondATC.exe)" : "Choose the X-Plane 12 folder",
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            Flash(_controller.SetFolder(key, path));
        }
    }

    private void ShowLiveLog()
    {
        if (_liveLog is { IsVisible: true })
        {
            _liveLog.Activate();
            return;
        }

        _liveLog = new LiveLogWindow();
        _liveLog.Show();
        Flash("LIVE LOG OPEN");
    }

    private void Flash(string? text)
    {
        _flash = text;
        _flashUntil = text is null ? default : DateTime.Now + FlashTime;
        Render();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var page = e.Key switch
        {
            Key.F1 => PanelPage.Status,
            Key.F2 => PanelPage.Setup,
            Key.F3 => PanelPage.Support,
            Key.F4 => PanelPage.About,
            _ => (PanelPage?)null,
        };
        if (page is { } p)
        {
            ShowPage(p);
            e.Handled = true;
            return;
        }

        var row = e.Key switch
        {
            >= Key.D1 and <= Key.D6 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad6 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (row >= 0)
        {
            _ = PressAsync(row, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private StackPanel Build()
    {
        // One grid for screen and keys, so each line select key sits exactly beside its data line.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("46,Auto,46"),
            Margin = new Thickness(10, 18, 10, 8),
        };
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(34)));
        for (var i = 0; i < 6; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(19)));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(28)));
        }

        grid.RowDefinitions.Add(new RowDefinition(new GridLength(36)));

        var screen = new Border { Background = Screen, CornerRadius = new CornerRadius(4), Margin = new Thickness(4, 0) };
        Grid.SetColumn(screen, 1);
        Grid.SetRowSpan(screen, 14);
        grid.Children.Add(screen);

        _title.HorizontalAlignment = HorizontalAlignment.Center;
        Place(grid, _title, 0);

        for (var i = 0; i < 6; i++)
        {
            _labelLeft[i] = Text(LabelSize, Colour(CduColor.Cyan));
            _labelRight[i] = Text(LabelSize, Colour(CduColor.Cyan));
            _dataLeft[i] = Text(DataSize);
            _dataRight[i] = Text(DataSize);
            Place(grid, Line(_labelLeft[i], _labelRight[i], null), 1 + (i * 2));
            Place(grid, Line(_dataLeft[i], _dataRight[i], i), 2 + (i * 2));

            var row = i;
            _lskLeft[i] = LineKey($"Line select {i + 1} left", () => PressAsync(row, right: false));
            _lskRight[i] = LineKey($"Line select {i + 1} right", () => PressAsync(row, right: true));
            Grid.SetRow(_lskLeft[i], 2 + (i * 2));
            Grid.SetRow(_lskRight[i], 2 + (i * 2));
            Grid.SetColumn(_lskRight[i], 2);
            grid.Children.Add(_lskLeft[i]);
            grid.Children.Add(_lskRight[i]);
        }

        // The divider sits at the top of the scratchpad row and the text below it, whatever the screen's scaling.
        var divider = new Border { BorderBrush = Brush("#1A2228"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(16, 0) };
        Place(grid, divider, 13);
        divider.VerticalAlignment = VerticalAlignment.Top;
        Place(grid, _scratch, 13);
        _scratch.Margin = new Thickness(16, 6, 16, 0);

        var keys = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 14) };
        foreach (var page in Enum.GetValues<PanelPage>())
        {
            var button = new Button
            {
                Content = page.ToString().ToUpperInvariant(),
                FontFamily = CduFont,
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Background = KeyFace,
                Foreground = KeyText,
                Padding = new Thickness(12, 9),
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(4),
            };
            AutomationProperties(button, page.ToString());
            button.Click += (_, _) => ShowPage(page);
            _pageKeys[page] = button;
            keys.Children.Add(button);
        }

        return new StackPanel { Children = { grid, keys } };
    }

    private Grid Line(TextBlock left, TextBlock right, int? row)
    {
        // Width fixed to 24 characters of the data font; the right half is flush with the screen's right edge.
        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Width = CharWidth() * CduPages.Width,
            Margin = new Thickness(16, 0),
            Background = Brushes.Transparent,
        };
        left.HorizontalAlignment = HorizontalAlignment.Left;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumnSpan(left, 2);
        Grid.SetColumnSpan(right, 2);
        line.Children.Add(left);
        line.Children.Add(right);
        if (row is { } r)
        {
            line.Cursor = new Cursor(StandardCursorType.Hand);
            line.PointerPressed += (_, e) => _ = PressAsync(r, e.GetPosition(line).X > line.Bounds.Width / 2);
        }

        return line;
    }

    private static double CharWidth()
    {
        var probe = new FormattedText("0", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(CduFont), DataSize, Brushes.White);
        return probe.WidthIncludingTrailingWhitespace;
    }

    private static Button LineKey(string name, Func<Task> press)
    {
        var button = new Button
        {
            Width = 30,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("#5A6168"),
            CornerRadius = new CornerRadius(3),
            Padding = default,
        };
        AutomationProperties(button, name);
        button.Click += (_, _) => _ = press();
        return button;
    }

    private static void AutomationProperties(Control control, string name) =>
        Avalonia.Automation.AutomationProperties.SetName(control, name);

    private static void Place(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
    }

    private static TextBlock Text(double size, IBrush? colour = null) => new()
    {
        FontFamily = CduFont,
        FontSize = size,
        Foreground = colour ?? Colour(CduColor.White),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void Paint(TextBlock block, CduCell cell)
    {
        block.Text = cell.Text;
        block.Foreground = Colour(cell.Color);
    }

    public static IBrush Colour(CduColor colour) => colour switch
    {
        CduColor.Green => Brush("#3EE07A"),
        CduColor.Cyan => Brush("#5FD3E6"),
        CduColor.Magenta => Brush("#FF6AD5"),
        CduColor.Amber => Brush("#FFB240"),
        _ => Brush("#EEF3F5"),
    };

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
