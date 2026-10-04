using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClearanceLink.Host.Panel;

/// <summary>The details for those who want them: ClearanceLink's status lines as they happen.</summary>
public sealed class LiveLogWindow : Window
{
    private readonly SelectableTextBlock _text = new()
    {
        FontFamily = PanelWindow.CduFont,
        FontSize = 12,
        Foreground = PanelWindow.Colour(CduColor.White),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(10),
    };

    private readonly ScrollViewer _scroll;
    private readonly List<string> _lines;

    public LiveLogWindow()
    {
        Title = "ClearanceLink live log";
        Width = 900;
        Height = 500;
        Background = new SolidColorBrush(Color.Parse("#05080A"));
        _lines = [.. PacketConsole.RecentStatusLines()];
        _scroll = new ScrollViewer { Content = _text };
        Content = _scroll;
        Show(_lines);
        PacketConsole.Line += OnLine;
        Closed += (_, _) => PacketConsole.Line -= OnLine;
    }

    private void OnLine(string line) => Dispatcher.UIThread.Post(() =>
    {
        _lines.Add(line);
        if (_lines.Count > PacketConsole.RecentLines)
        {
            _lines.RemoveAt(0);
        }

        Show(_lines);
    });

    private void Show(List<string> lines)
    {
        var atEnd = _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - 4;
        _text.Text = string.Join(Environment.NewLine, lines);
        if (atEnd)
        {
            _scroll.ScrollToEnd();
        }
    }
}
