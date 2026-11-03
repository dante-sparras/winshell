using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>
/// The controls the arrows walk on one taskbar, in order: Start is 0 and Search is 1, then the apps, then the tray and
/// the clock that are on the bar.
/// </summary>
internal sealed class TaskbarStops(TaskbarView bar, Button start, Button search, ItemsRepeater apps, Panel corner)
{
    public int Count => 2 + bar.Model.Tasks.Count + CornerStops().Count;

    /// <summary>Where Win+T lands.</summary>
    public int FirstApp => TaskbarKeyboard.FirstApp(bar.Model.Tasks.Count, Count);

    public Control? At(int index)
    {
        if (index == 0)
            return start;
        if (index == 1)
            return search;
        var count = bar.Model.Tasks.Count;
        if (index < 2 + count)
            return App(index - 2);
        var corners = CornerStops();
        var at = index - 2 - count;
        return (uint)at < (uint)corners.Count ? corners[at] : null;
    }

    /// <summary>The button of the app at <paramref name="index"/>. One scrolled out of view is made once the bar is loaded.</summary>
    public TaskButton? App(int index)
    {
        if ((uint)index >= (uint)bar.Model.Tasks.Count)
            return null;
        var element = apps.TryGetElement(index);
        if (element is null && bar.IsLoaded)
        {
            try
            {
                element = apps.GetOrCreateElement(index);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        return element as TaskButton;
    }

    /// <summary>The stop that has the keyboard, or <see cref="FirstApp"/> when none has it.</summary>
    public int Focused()
    {
        var fallback = FirstApp;
        if (FocusedElement() is not { } focused)
            return fallback;
        if (Contains(start, focused))
            return 0;
        if (Contains(search, focused))
            return 1;
        var app = AppOf(focused);
        if (app >= 0)
            return 2 + app;
        var corners = CornerStops();
        for (var i = 0; i < corners.Count; i++)
        {
            if (Contains(corners[i], focused))
                return 2 + bar.Model.Tasks.Count + i;
        }
        return fallback;
    }

    /// <summary>The app whose button has the keyboard, or -1.</summary>
    public int FocusedApp() => FocusedElement() is { } focused ? AppOf(focused) : -1;

    /// <summary>A button of this bar, and not one inside a menu or the hidden-icons flyout.</summary>
    public bool IsStop(DependencyObject source)
    {
        var inMenu = false;
        Button? button = null;
        for (DependencyObject? node = source; node is not null && !ReferenceEquals(node, bar); node = VisualTreeHelper.GetParent(node))
        {
            if (node is FlyoutPresenter or MenuFlyoutPresenter or Popup)
                inMenu = true;
            if (button is null && node is Button found)
                button = found;
        }
        if (button is null || inMenu)
            return false;
        if (ReferenceEquals(button, start) || ReferenceEquals(button, search) || button is TaskButton)
            return true;
        return CornerStops().Contains(button);
    }

    private DependencyObject? FocusedElement() =>
        bar.XamlRoot is null ? null : FocusManager.GetFocusedElement(bar.XamlRoot) as DependencyObject;

    private int AppOf(DependencyObject focused)
    {
        for (var i = 0; i < bar.Model.Tasks.Count; i++)
        {
            if (apps.TryGetElement(i) is DependencyObject element && Contains(element, focused))
                return i;
        }
        return -1;
    }

    /// <summary>The tray and the clock. A flyout's buttons, and a control that is collapsed, are not stops.</summary>
    private List<Button> CornerStops()
    {
        var stops = new List<Button>();
        Collect(corner, stops);
        return stops;

        static void Collect(DependencyObject node, List<Button> stops)
        {
            if (node is FlyoutPresenter or Popup)
                return;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is UIElement { Visibility: not Visibility.Visible })
                    continue;
                if (child is Button button)
                {
                    if (button.IsTabStop)
                        stops.Add(button);
                    continue;
                }
                Collect(child, stops);
            }
        }
    }

    private static bool Contains(DependencyObject root, DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
