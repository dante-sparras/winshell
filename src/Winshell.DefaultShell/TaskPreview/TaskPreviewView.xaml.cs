using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Winshell;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The preview popup's content. Cards are built here, not in a repeater, so every window is real at once and the
/// popup is the size of that content. The title and the close button sit above the live image: a thumbnail is drawn
/// on top of the XAML, and nothing can overlap it.
/// </summary>
public sealed partial class TaskPreviewView : UserControl
{
    private readonly TaskPreview _model;
    private readonly List<Slot> _slots = [];
    private bool _slotsListed;

    public TaskPreviewView(TaskPreview model)
    {
        _model = model;
        InitializeComponent();
        _model.PropertyChanged += OnModel;
        Root.PointerEntered += (_, _) => _model.PointerInside(true);
        Root.PointerExited += OnRootExited;
        PreviewKeyDown += OnPreviewKey;
        ListHost.SizeChanged += (_, _) => SyncScroll();
    }

    /// <summary>Drops the subscription. The model may outlive one rebuild of this view.</summary>
    public void Release() => _model.PropertyChanged -= OnModel;

    /// <summary>Builds the cards or the list for the model as it is now, and puts the keyboard where the model says.</summary>
    public void Live()
    {
        if (!_model.IsOpen)
            return;
        if (!_model.Running)
        {
            ShowName();
            return;
        }
        if (_model.Listed)
            ShowList();
        else
            ShowCards();
        ApplyFocus();
    }

    /// <summary>The popup's size. A running app is the model's size. A name is measured from the text.</summary>
    public Size SizeForPopup()
    {
        if (_model.Running)
            return new Size(_model.Width, _model.Height);
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = DesiredSize.Width;
        var height = DesiredSize.Height;
        if (width < 1 || height < 1)
        {
            NameText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = NameText.DesiredSize.Width + 24;
            height = NameText.DesiredSize.Height + 16;
        }
        return new Size(width, height);
    }

    /// <summary>Moves the keyboard to the focused card or its close button.</summary>
    public void ApplyFocus()
    {
        if (!_model.GrabFocus || !_model.Running || _model.FocusIndex < 0 || _model.FocusIndex >= _slots.Count)
            return;
        UpdateChrome();
        var slot = _slots[_model.FocusIndex];
        var target = _model.FocusClose ? (Control)slot.Close : slot.Card;
        target.Visibility = Visibility.Visible;
        if (target.FocusState == FocusState.Unfocused)
            target.Focus(FocusState.Programmatic);
    }

    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskPreview.IsOpen))
        {
            if (_model.IsOpen)
                Live();
            else
                Clear();
            return;
        }
        if (!_model.IsOpen)
            return;
        if (e.PropertyName is nameof(TaskPreview.PeekWindow) or nameof(TaskPreview.HoveredWindow))
        {
            UpdateChrome();
            return;
        }
        if (e.PropertyName is nameof(TaskPreview.Windows) or nameof(TaskPreview.Listed) or nameof(TaskPreview.Width) or nameof(TaskPreview.CardWidth) or nameof(TaskPreview.ListWidth))
            Live();
        else if (e.PropertyName is nameof(TaskPreview.FocusIndex) or nameof(TaskPreview.FocusClose) or nameof(TaskPreview.GrabFocus))
        {
            UpdateChrome();
            ApplyFocus();
        }
        else if (e.PropertyName == nameof(TaskPreview.ScrollY))
            SyncScroll();
    }

    private void ShowName()
    {
        ClearSlots();
        Cards.Visibility = Visibility.Collapsed;
        ListHost.Visibility = Visibility.Collapsed;
        NameText.Text = _model.Name;
        NameText.Visibility = Visibility.Visible;
        Root.Padding = new Thickness(12, 8, 12, 8);
        Width = double.NaN;
        Height = double.NaN;
        AutomationProperties.SetName(this, _model.Name);
    }

    private void ShowCards()
    {
        NameText.Visibility = Visibility.Collapsed;
        ListHost.Visibility = Visibility.Collapsed;
        Cards.Visibility = Visibility.Visible;
        Root.Padding = new Thickness(8);
        Width = _model.Width;
        Height = _model.Height;
        if (!SameSlots(listed: false))
            Rebuild(listed: false);
        foreach (var slot in _slots)
        {
            slot.Root.Width = _model.CardWidth;
            slot.Root.Height = _model.CardHeight;
            if (slot.Thumbnail is not null)
            {
                slot.Thumbnail.Width = Math.Max(0, _model.CardWidth - 16);
                slot.Thumbnail.Height = Math.Max(0, _model.CardHeight - 40);
            }
        }
        UpdateChrome();
        AutomationProperties.SetName(this, _model.Name);
    }

    private void ShowList()
    {
        NameText.Visibility = Visibility.Collapsed;
        Cards.Visibility = Visibility.Collapsed;
        ListHost.Visibility = Visibility.Visible;
        Root.Padding = new Thickness(8);
        Width = _model.Width;
        Height = _model.Height;
        ListHost.Width = _model.ListWidth;
        ListHost.Height = _model.ViewportHeight;
        if (!SameSlots(listed: true))
            Rebuild(listed: true);
        foreach (var slot in _slots)
        {
            slot.Root.Width = _model.ListWidth;
            slot.Root.Height = TaskPreview.RowHeight;
        }
        UpdateChrome();
        SyncScroll();
        AutomationProperties.SetName(this, _model.Name);
    }

    private bool SameSlots(bool listed)
    {
        if (_slotsListed != listed || _slots.Count != _model.Windows.Count)
            return false;
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!Equals(_slots[i].Window, _model.Windows[i]))
                return false;
        }
        return true;
    }

    private void Rebuild(bool listed)
    {
        ClearSlots();
        _slotsListed = listed;
        var panel = listed ? Rows : Cards;
        foreach (var window in _model.Windows)
        {
            var slot = listed ? Row(window) : Card(window);
            _slots.Add(slot);
            panel.Children.Add(slot.Root);
        }
    }

    private Slot Card(ITaskWindow window)
    {
        var root = new Grid();
        var hover = new Border { Style = (Style)Resources["PreviewHover"] };
        var icon = IconImage(new Thickness(8, 8, 0, 0));
        var title = new TextBlock
        {
            Style = (Style)Resources["PreviewCaption"],
            Margin = new Thickness(32, 6, 32, 0),
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        // The image starts below the title and the close button. It is painted over the XAML.
        var thumbnail = new WindowThumbnail
        {
            Margin = new Thickness(8, 32, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Window = window is TaskWindow real ? real.Window : null,
        };
        var content = new Grid();
        content.Children.Add(icon);
        content.Children.Add(title);
        content.Children.Add(thumbnail);
        var card = Face(content, window);
        var close = new PreviewCloseButton
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed,
        };
        close.Click += (_, _) => _model.Close(window);
        root.Children.Add(hover);
        root.Children.Add(card);
        root.Children.Add(close);
        WatchPointer(root, window);
        return new Slot(window, root, card, close, title, icon, hover, thumbnail);
    }

    private Slot Row(ITaskWindow window)
    {
        var root = new Grid { Height = TaskPreview.RowHeight };
        var hover = new Border { Style = (Style)Resources["PreviewHover"] };
        var icon = IconImage(new Thickness(12, 0, 0, 0));
        icon.VerticalAlignment = VerticalAlignment.Center;
        var title = new TextBlock
        {
            Style = (Style)Resources["PreviewBody"],
            Margin = new Thickness(40, 0, 36, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var content = new Grid();
        content.Children.Add(icon);
        content.Children.Add(title);
        var card = Face(content, window);
        var close = new PreviewCloseButton
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        close.Click += (_, _) => _model.Close(window);
        root.Children.Add(hover);
        root.Children.Add(card);
        root.Children.Add(close);
        WatchPointer(root, window);
        return new Slot(window, root, card, close, title, icon, hover, null);
    }

    private Button Face(UIElement content, ITaskWindow window)
    {
        var card = new Button { Style = (Style)Resources["PreviewCardButton"], Content = content };
        card.Click += (_, _) => _model.Activate(window);
        return card;
    }

    private static Image IconImage(Thickness margin) => new()
    {
        Width = 16,
        Height = 16,
        Margin = margin,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Stretch = Stretch.Uniform,
    };

    private void WatchPointer(Grid root, ITaskWindow window)
    {
        root.PointerEntered += (_, _) =>
        {
            _model.PointerInside(true);
            _model.PointerOn(window);
        };
        root.PointerExited += (_, e) =>
        {
            var point = e.GetCurrentPoint(root).Position;
            if (point.X >= 0 && point.Y >= 0 && point.X < root.ActualWidth && point.Y < root.ActualHeight)
                return;
            _model.PointerOn(null);
        };
    }

    private void UpdateChrome()
    {
        var pixels = ShellIcon.Pixels(XamlRoot, 16);
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var title = slot.Window.Title;
            slot.Title.Text = title;
            AutomationProperties.SetName(slot.Card, title);
            AutomationProperties.SetName(slot.Close, TaskPreview.CloseName(title));
            slot.Close.Visibility = _model.ShowsClose(i) ? Visibility.Visible : Visibility.Collapsed;
            slot.Hover.Visibility = _model.ShowsClose(i) ? Visibility.Visible : Visibility.Collapsed;
            if (slot.Thumbnail is not null)
                slot.Thumbnail.Peek = _model.PeekWindow is not null && Equals(_model.PeekWindow, slot.Window);
            if (slot.IconPath == slot.Window.Icon)
                continue;
            slot.IconPath = slot.Window.Icon;
            slot.Icon.Source = slot.IconPath.Length == 0 ? null : ShellIcon.Load(slot.IconPath, pixels);
        }
    }

    private void SyncScroll()
    {
        if (!_model.Listed || ListHost.Visibility != Visibility.Visible)
            return;
        ListHost.ChangeView(null, _model.ScrollY, null, true);
    }

    private void Clear()
    {
        ClearSlots();
        NameText.Visibility = Visibility.Collapsed;
        Cards.Visibility = Visibility.Collapsed;
        ListHost.Visibility = Visibility.Collapsed;
        Width = double.NaN;
        Height = double.NaN;
    }

    private void ClearSlots()
    {
        _slots.Clear();
        Cards.Children.Clear();
        Rows.Children.Clear();
    }

    private void OnRootExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root).Position;
        if (point.X >= 0 && point.Y >= 0 && point.X < Root.ActualWidth && point.Y < Root.ActualHeight)
            return;
        _model.PointerInside(false);
    }

    private void OnPreviewKey(object sender, KeyRoutedEventArgs e)
    {
        var handled = e.Key switch
        {
            VirtualKey.Escape => Escape(),
            VirtualKey.Left => _model.MoveHorizontal(-1),
            VirtualKey.Right => _model.MoveHorizontal(1),
            VirtualKey.Up => _model.MoveVertical(-1),
            VirtualKey.Down => _model.MoveVertical(1),
            VirtualKey.Tab => _model.Tab(!Shift()),
            VirtualKey.Enter => _model.ActivateFocused(),
            VirtualKey.Delete => _model.PressDelete(),
            _ => false,
        };
        if (!handled)
            return;
        e.Handled = true;
        if (_model.IsOpen)
            ApplyFocus();
    }

    private bool Escape()
    {
        if (!_model.IsOpen)
            return false;
        _model.Escape();
        return true;
    }

    private static bool Shift() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    private sealed class Slot(
        ITaskWindow window,
        Grid root,
        Button card,
        PreviewCloseButton close,
        TextBlock title,
        Image icon,
        Border hover,
        WindowThumbnail? thumbnail)
    {
        public ITaskWindow Window { get; } = window;

        public Grid Root { get; } = root;

        public Button Card { get; } = card;

        public PreviewCloseButton Close { get; } = close;

        public TextBlock Title { get; } = title;

        public Image Icon { get; } = icon;

        public Border Hover { get; } = hover;

        public WindowThumbnail? Thumbnail { get; } = thumbnail;

        public string IconPath { get; set; } = "";
    }
}
