using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ClearanceLink.Host.Panel;

/// <summary>
/// The FMS-style panel: a 24-column screen with six line select keys each side and page keys below. Keys 1-6 press
/// the left keys, Shift+1-6 the right ones, F1-F5 change page; clicking either half of a row presses its key too.
/// </summary>
public sealed class PanelWindow : Window
{
    public static readonly FontFamily CduFont = new("avares://ClearanceLink/Panel/Fonts#B612 Mono");

    /// <summary>The same icon as the exe (Assets/ClearanceLink.ico), for the title bar and taskbar.</summary>
    public static WindowIcon AppIcon() => new(Avalonia.Platform.AssetLoader.Open(new Uri("avares://ClearanceLink/Assets/ClearanceLink.ico")));

    private const double DataSize = 19;
    private const double LabelSize = 13.5;
    private const double MessageSize = 15;
    private static readonly TimeSpan FlashTime = TimeSpan.FromSeconds(4);

    /// <summary>How long a second close stops a running conversion (after that, closing asks again).</summary>
    public static readonly TimeSpan CloseAgainTime = TimeSpan.FromSeconds(10);

    /// <summary>The scratchpad when closing would stop a conversion.</summary>
    public const string CloseAgainText = "CLOSE AGAIN TO STOP";

    private static readonly IBrush Bezel = Brush("#2A2F33");
    private static readonly IBrush Screen = Brush("#05080A");
    private static readonly IBrush KeyFace = Brush("#454C52");
    private static readonly IBrush KeyText = Brush("#E8ECEF");

    private readonly PanelController? _controller;
    private readonly Func<PanelState> _state;
    private readonly TextBlock _title = Text(DataSize);
    private readonly TextBlock _scratch = Text(DataSize);
    private readonly TextBlock _message = Text(MessageSize);
    private readonly TextBlock[] _labelLeft = new TextBlock[6];
    private readonly TextBlock[] _labelRight = new TextBlock[6];
    private readonly TextBlock[] _dataLeft = new TextBlock[6];
    private readonly TextBlock[] _dataRight = new TextBlock[6];
    private readonly Control[] _infoLeft = new Control[6];
    private readonly Control[] _infoRight = new Control[6];
    private readonly Button[] _lskLeft = new Button[6];
    private readonly Button[] _lskRight = new Button[6];
    private readonly Dictionary<PanelPage, Button> _pageKeys = [];
    private readonly DispatcherTimer _timer;
    private CduPage? _shown;
    private string? _flash;
    private DateTime _flashUntil;
    private LiveLogWindow? _liveLog;
    private DateTime _closeArmedUntil;

    public PanelWindow(PanelController controller)
        : this(controller.State)
    {
        _controller = controller;
    }

    /// <summary>For tests: a window over a fixed state, with no server behind it.</summary>
    public PanelWindow(Func<PanelState> state)
    {
        _state = state;
        Title = "ClearanceLink";
        Icon = AppIcon();
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Bezel;
        Content = Build();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Render());
        Opened += (_, _) => _timer.Start();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _liveLog?.Close();
        };
        KeyDown += OnKeyDown;
        Render();
    }

    public PanelPage Page { get; private set; } = PanelPage.Status;

    /// <summary>Opens on the getting-started pages instead of STATUS (first run).</summary>
    public bool StartWithGettingStarted
    {
        init
        {
            if (value)
            {
                ShowPage(CduPages.StartPages[0]);
            }
        }
    }

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

            // Help: the explanation shows on hover over the "i" only (not the whole row, or moving the mouse across
            // the screen would open one after another), on the side of the label it explains.
            _infoLeft[i].IsVisible = row.Mark == HelpMark.Left;
            _infoRight[i].IsVisible = row.Mark == HelpMark.Right;
            ToolTip.SetTip(_infoLeft[i], row.Help);
            ToolTip.SetTip(_infoRight[i], row.Help);
        }

        ShowMessage(page);
        Paint(_scratch, page.Scratchpad);
        foreach (var (key, button) in _pageKeys)
        {
            button.Foreground = key == Page ? Colour(CduColor.Green) : KeyText;
        }
    }

    /// <summary>Draws the page's message over the empty rows at the top, one paragraph per cell.</summary>
    private void ShowMessage(CduPage page)
    {
        var rows = page.Rows.TakeWhile(r => r == CduRow.Empty).Count();
        _message.IsVisible = page.Message is { Count: > 0 } && rows > 0;
        if (!_message.IsVisible)
        {
            return;
        }

        Grid.SetRowSpan(_message, rows * 2);
        _message.Inlines!.Clear();
        foreach (var (cell, i) in page.Message!.Select((c, i) => (c, i)))
        {
            if (i > 0)
            {
                _message.Inlines.Add(new LineBreak());
                _message.Inlines.Add(new Run("\n") { FontSize = MessageSize / 2 });
            }

            _message.Inlines.Add(new Run(cell.Text) { Foreground = Colour(cell.Color) });
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
            case CduKey.GoFlightPlan:
                ShowPage(PanelPage.FlightPlan);
                return;
            case CduKey.GoPush:
                ShowPage(PanelPage.Push);
                return;
            case CduKey.ChooseApp:
                await ChooseAppAsync();
                return;
            case CduKey.ChooseXPlaneFolder:
            case CduKey.ChooseFsltlFolder:
                await ChooseFolderAsync(key);
                return;
            case CduKey.ShowLiveLog:
                ShowLiveLog();
                return;
            case CduKey.ShowGettingStarted:
                ShowPage(CduPages.StartPages[0]);
                return;
            case CduKey.GoModels:
            case CduKey.GoTraffic:
                ShowPage(PanelPage.Traffic);
                return;
            case CduKey.GoFsltl:
                ShowPage(PanelPage.Fsltl);
                return;
            case CduKey.Next:
            case CduKey.Back:
                var at = CduPages.StartPages.IndexOf(Page);
                var to = at + (key == CduKey.Next ? 1 : -1);
                ShowPage(at < 0 || to < 0 ? PanelPage.Status : CduPages.StartPages[Math.Min(to, CduPages.StartPages.Length - 1)]);
                return;
            case CduKey.FinishGettingStarted:
                ShowPage(PanelPage.Status);
                if (_controller is not null)
                {
                    Flash(await _controller.FinishGettingStartedAsync());
                }

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

        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = key switch
            {
                CduKey.ChooseFsltlFolder => "Choose FSLTL's fsltl-traffic-base folder (in MSFS's Community folder)",
                _ => "Choose the X-Plane 12 folder",
            },
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            var result = _controller.SetFolder(key, path);
            // A new FSLTL folder is checked in the background: FSLTL CHECK shows what was found in it.
            if (key == CduKey.ChooseFsltlFolder && result == PanelController.FsltlSaved)
            {
                ShowPage(PanelPage.Fsltl);
            }

            Flash(result);
        }
    }

    private async Task ChooseAppAsync()
    {
        if (_controller is null)
        {
            return;
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the ATC app to bridge (its .exe)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Programs (*.exe)") { Patterns = ["*.exe"] }],
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
        {
            Flash(_controller.SetFolder(CduKey.ChooseApp, path));
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

    private void Flash(string? text, TimeSpan? time = null)
    {
        _flash = text;
        _flashUntil = text is null ? default : DateTime.Now + (time ?? FlashTime);
        Render();
    }

    /// <summary>
    /// Closing while FSLTL models convert would cut the conversion off. The first close is held back: the window shows
    /// the conversion's page with CLOSE AGAIN TO STOP; a second close within 10 s stops the conversion (the models
    /// converted before stay as they were) and closes. This is the CDU's own way of asking (a scratchpad message, like
    /// every other answer) rather than a separate dialog window, which would need its own styling, owner and
    /// keyboard handling, and can end up behind the panel. Windows shutting down or the app exiting never waits.
    /// </summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_state().FsltlConverting)
        {
            return;
        }

        if (DateTime.Now < _closeArmedUntil || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
        {
            _controller?.StopConverting();
            return;
        }

        e.Cancel = true;
        _closeArmedUntil = DateTime.Now + CloseAgainTime;
        Page = PanelPage.StartModels;
        Flash(CloseAgainText, CloseAgainTime);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var page = e.Key switch
        {
            Key.F1 => PanelPage.Status,
            Key.F2 => PanelPage.Setup,
            Key.F3 => PanelPage.Support,
            Key.F4 => PanelPage.About,
            Key.F5 => PanelPage.FlightPlan,
            Key.F6 => PanelPage.Push,
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
            _infoLeft[i] = InfoMark(new Thickness(5, 1, 0, 0));
            _infoRight[i] = InfoMark(new Thickness(5, 1, 0, 0));
            var labelLine = Line(
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _labelLeft[i], _infoLeft[i] } },
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _labelRight[i], _infoRight[i] } },
                null);
            var dataLine = Line(_dataLeft[i], _dataRight[i], i);
            Place(grid, labelLine, 1 + (i * 2));
            Place(grid, dataLine, 2 + (i * 2));

            var row = i;
            _lskLeft[i] = LineKey($"Line select {i + 1} left", () => PressAsync(row, right: false));
            _lskRight[i] = LineKey($"Line select {i + 1} right", () => PressAsync(row, right: true));
            Grid.SetRow(_lskLeft[i], 2 + (i * 2));
            Grid.SetRow(_lskRight[i], 2 + (i * 2));
            Grid.SetColumn(_lskRight[i], 2);
            grid.Children.Add(_lskLeft[i]);
            grid.Children.Add(_lskRight[i]);
            foreach (var mark in new[] { _infoLeft[i], _infoRight[i] })
            {
                ToolTip.SetShowDelay(mark, 250);
                ToolTip.SetPlacement(mark, PlacementMode.Bottom);
            }
        }

        // Messages (getting-started pages) start under the title and wrap to the 24-column width.
        _message.TextWrapping = TextWrapping.Wrap;
        _message.LineHeight = MessageSize * 1.35;
        _message.Width = CharWidth() * CduPages.Width;
        _message.Margin = new Thickness(16, 4, 16, 0);
        Place(grid, _message, 1);
        _message.VerticalAlignment = VerticalAlignment.Top;
        _message.HorizontalAlignment = HorizontalAlignment.Left;

        // The divider sits at the top of the scratchpad row and the text below it, whatever the screen's scaling.
        var divider = new Border { BorderBrush = Brush("#1A2228"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(16, 0) };
        Place(grid, divider, 13);
        divider.VerticalAlignment = VerticalAlignment.Top;
        Place(grid, _scratch, 13);
        _scratch.Margin = new Thickness(16, 6, 16, 0);

        var keys = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 14) };
        foreach (var page in CduPages.MainPages)
        {
            var button = new Button
            {
                Content = CduPages.KeyName(page),
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

    /// <summary>A small dim "i" in a circle beside a row's label: hovering it explains the row.</summary>
    private static Border InfoMark(Thickness margin) => new()
    {
        Width = 12,
        Height = 12,
        CornerRadius = new CornerRadius(6),
        BorderBrush = Brush("#3B8794"),
        BorderThickness = new Thickness(1),
        Background = Brushes.Transparent, // the whole circle answers the mouse, not just its outline and letter
        Cursor = new Cursor(StandardCursorType.Help),
        Margin = margin,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
        Child = new TextBlock
        {
            Text = "i",
            FontFamily = CduFont,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#5FB3C0"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private Grid Line(Control left, Control right, int? row)
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
