using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>One app on the taskbar. A left click does what <see cref="Tasks.Click"/> says. Shift or the middle button opens a new window.</summary>
public sealed partial class TaskButton : Button
{
    private readonly List<INotifyPropertyChanged> _watched = [];
    private TaskItem? _item;
    private MenuFlyout? _menu;
    private string _iconPath = "";
    private int _iconSize;
    private bool _pressed;

    public TaskButton()
    {
        InitializeComponent();
        Click += OnClick;
        ContextRequested += OnContextRequested;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerEnded;
        PointerCaptureLost += OnPointerEnded;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TaskItem? Item
    {
        get => _item;
        set
        {
            // The app left the bar, so its menu goes with it.
            if (value?.Key != _item?.Key)
                _menu?.Hide();
            _item = value;
            Watch(value);
            Apply();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not null)
            XamlRoot.Changed += OnRootChanged;
        Watch(_item);
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is not null)
            XamlRoot.Changed -= OnRootChanged;
        Watch(null);
        _menu?.Hide();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Apply();

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (Item is not { } task || Model() is not { } model)
            return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shift)
            model.Launch(task);
        else
            model.Click(task);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Item is not { } task || Model() is not { } model)
            return;
        args.Handled = true;
        // The rows are read once. What a row does reads the app as it is when it is chosen.
        var menu = TaskMenuFlyout.Create(Tasks.Name(task), model.MenuRows(task), id =>
        {
            if (Item is { } current)
                model.Choose(current, id);
        }, Pixels(16));
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu))
                _menu = null;
        };
        _menu = menu;
        menu.ShowAt(this);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;
        _pressed = true;
        Apply();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var middle = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonReleased;
        EndPress();
        if (!middle)
            return;
        if (Item is { } task && Model() is { } model)
            model.Launch(task);
        e.Handled = true;
    }

    private void OnPointerEnded(object sender, PointerRoutedEventArgs e) => EndPress();

    private void EndPress()
    {
        if (!_pressed)
            return;
        _pressed = false;
        Apply();
    }

    private TaskbarModel? Model()
    {
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TaskbarView view)
                return view.Model;
        }
        return null;
    }

    private void Watch(TaskItem? task)
    {
        foreach (var source in _watched)
            source.PropertyChanged -= OnWindowChanged;
        _watched.Clear();
        if (task is null)
            return;
        Follow(task.Entry);
        foreach (var window in task.Windows)
            Follow(window);
    }

    private void Follow(object? instance)
    {
        if (instance is not INotifyPropertyChanged source)
            return;
        source.PropertyChanged += OnWindowChanged;
        _watched.Add(source);
    }

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply()
    {
        var task = Item;
        var mark = TaskbarModel.Indicator.Of(task);
        AutomationProperties.SetName(this, task is null ? "" : Tasks.AccessibleName(task));
        ShowIcon(task);
        var showFill = !_pressed;
        FillActive.Visibility = Shown(showFill && mark.Active && !mark.Attention);
        FillAttention.Visibility = Shown(showFill && mark.Attention);
        Stroke.Visibility = Shown(mark.Active || mark.Attention);
        MarkRest.Visibility = Shown(mark.Running && !mark.Active && !mark.Attention);
        MarkActive.Visibility = Shown(mark.Running && mark.Active && !mark.Attention);
        MarkAttention.Width = mark.LineWidth;
        MarkAttention.Visibility = Shown(mark.Running && mark.Attention);
    }

    private void ShowIcon(TaskItem? task)
    {
        var path = task is null ? "" : task.Icon;
        var size = Pixels(24);
        if (path == _iconPath && size == _iconSize)
            return;
        _iconPath = path;
        _iconSize = size;
        Icon.Source = path.Length == 0 ? null : ShellIcon.Load(path, size);
    }

    private int Pixels(int size)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        if (scale < 1)
            scale = 1;
        return Math.Max(1, (int)Math.Ceiling(size * scale));
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
