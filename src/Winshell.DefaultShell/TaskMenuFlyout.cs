using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>A WinUI menu for the rows <see cref="TaskMenu"/> describes. The bar is a short panel, so the menu opens above it.</summary>
internal static class TaskMenuFlyout
{
    private static readonly FontFamily Icons = new("Segoe Fluent Icons");

    public static MenuFlyout Create(string name, IReadOnlyList<TaskMenuRow> rows, Action<string> choose, int iconSize)
    {
        var presenter = new Style(typeof(MenuFlyoutPresenter));
        presenter.Setters.Add(new Setter(AutomationProperties.NameProperty, name));
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.Top,
            ShouldConstrainToRootBounds = false,
            MenuFlyoutPresenterStyle = presenter,
        };
        foreach (var row in rows)
        {
            var item = new MenuFlyoutItem { Text = row.Label, IsEnabled = row.Enabled, Icon = Icon(row, iconSize) };
            item.Click += (_, _) => choose(row.Id);
            menu.Items.Add(item);
        }
        return menu;
    }

    private static IconElement? Icon(TaskMenuRow row, int iconSize)
    {
        if (row.Icon.Length > 0)
        {
            var source = ShellIcon.Load(row.Icon, iconSize);
            return source is null ? null : new ImageIcon { Source = source };
        }
        if (row.Glyph.Length > 0)
            return new FontIcon { Glyph = row.Glyph, FontSize = 16, FontFamily = Icons };
        return null;
    }
}
