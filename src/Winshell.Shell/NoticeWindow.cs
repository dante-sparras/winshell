using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Winshell;

/// <summary>
/// A notice, drawn like a Windows 11 notification: bottom right of the main screen, above the taskbar, on Acrylic. It does
/// not take the keyboard when it appears. A click on it does, and then Tab moves between its buttons and Escape closes
/// it. Pointing at it, or having the keyboard, holds a timed notice open. It is built here and not in XAML so that it
/// needs nothing from a configuration.
/// </summary>
internal sealed class NoticeWindow : IDisposable
{
    private const double NoticeWidth = 364;

    private readonly IEventLoop _loop;
    private readonly TimeSpan _timeout;
    private readonly Action _dismiss;
    private readonly PanelWindow _window;
    private IDisposable? _timer;
    private bool _pointer;
    private bool _keyboard;

    public NoticeWindow(Notice notice, Action act, Action dismiss, IEventLoop loop)
    {
        _loop = loop;
        _timeout = notice.Timeout;
        _dismiss = dismiss;
        var content = Content(notice, act, dismiss);
        content.Measure(new Size(NoticeWidth, double.PositiveInfinity));
        _window = new PanelWindow
        {
            Anchors = Edges.Bottom | Edges.Right,
            Margins = new Thickness(0, 0, 12, 12),
            Width = NoticeWidth,
            Height = Math.Ceiling(content.DesiredSize.Height),
            AboveWindows = true,
            Focusable = true,
            Backdrop = WindowBackdrop.Acrylic,
            Corners = WindowCorners.Round,
            Content = content
        };
        _window.Visible = true;
        Hold();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _window.Dispose();
    }

    private FrameworkElement Content(Notice notice, Action act, Action dismiss)
    {
        var said = string.Join(". ",
            new[] { notice.Title, notice.Detail, notice.Body, notice.Note }.Where(text => text.Length > 0));
        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 10 }, Width = 32, Height = 24, Padding = default,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AutomationProperties.SetName(close, "Dismiss");
        close.Click += (_, _) => dismiss();
        var action = new Button
        {
            Content = notice.ActionText, Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        action.Click += (_, _) => act();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.Children.Add(Text("Winshell", "CaptionTextBlockStyle", 0.8));
        header.Children.Add(close);
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(header);
        stack.Children.Add(Text(notice.Title, "BodyStrongTextBlockStyle", 1));
        if (notice.Detail.Length > 0)
            stack.Children.Add(Text(notice.Detail, "CaptionTextBlockStyle", 0.8));
        stack.Children.Add(Text(notice.Body, "BodyTextBlockStyle", 1));
        stack.Children.Add(Text(notice.Note, "CaptionTextBlockStyle", 0.8));
        action.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(action);
        // Transparent, so a click on the text or between the buttons still lands on the notice.
        var root = new Grid
            { Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        root.Children.Add(stack);
        AutomationProperties.SetName(root, said);
        AutomationProperties.SetLiveSetting(root, AutomationLiveSetting.Assertive);
        root.Loaded += (_, _) =>
        {
            // A button has no template until now, so what was measured before the window opened came out short.
            root.Measure(new Size(NoticeWidth, double.PositiveInfinity));
            _window.Height = Math.Ceiling(root.DesiredSize.Height);
            FrameworkElementAutomationPeer.CreatePeerForElement(root)?
                .RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantAll,
                    said, "WinshellNotice");
        };
        root.PointerPressed += (_, _) =>
        {
            _window.Focus();
            close.Focus(FocusState.Programmatic);
        };
        root.PointerEntered += (_, _) => Held(ref _pointer, true);
        root.PointerExited += (_, _) => Held(ref _pointer, false);
        root.GotFocus += (_, _) => Held(ref _keyboard, true);
        root.LostFocus += (_, _) => Held(ref _keyboard, false);
        root.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape)
                return;
            e.Handled = true;
            dismiss();
        };
        return root;
    }

    private static TextBlock Text(string text, string style, double opacity)
    {
        return new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources[style],
            Opacity = opacity,
            TextWrapping = TextWrapping.WrapWholeWords
        };
    }

    private void Held(ref bool flag, bool on)
    {
        flag = on;
        Hold();
    }

    /// <summary>Runs the time down while no one is at the notice, from the start each time they leave.</summary>
    private void Hold()
    {
        _timer?.Dispose();
        _timer = _timeout > TimeSpan.Zero && !_pointer && !_keyboard ? _loop.Schedule(_timeout, _dismiss) : null;
    }
}
