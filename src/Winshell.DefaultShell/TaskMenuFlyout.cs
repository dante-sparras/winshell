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

    public static MenuFlyout Create(string name)
    {
        var menu = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.Top,
            ShouldConstrainToRootBounds = false,
        };
        Name(menu, name);
        return menu;
    }

    public static void Name(MenuFlyout menu, string name)
    {
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(AutomationProperties.NameProperty, name));
        menu.MenuFlyoutPresenterStyle = style;
    }

    public static void Fill(MenuFlyout menu, IReadOnlyList<TaskMenuRow> rows, Action<string> invoke, int iconSize)
    {
        if (menu.Items.Count == rows.Count && SameIds(menu, rows))
        {
            for (var i = 0; i < rows.Count; i++)
                Apply((MenuFlyoutItem)menu.Items[i], rows[i], iconSize);
            return;
        }
        menu.Items.Clear();
        foreach (var row in rows)
        {
            var item = new MenuFlyoutItem();
            var id = row.Id;
            item.Click += (_, _) => invoke(id);
            Apply(item, row, iconSize);
            menu.Items.Add(item);
        }
    }

    private static bool SameIds(MenuFlyout menu, IReadOnlyList<TaskMenuRow> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (menu.Items[i] is not MenuFlyoutItem item || item.Tag is not string id || id != rows[i].Id)
                return false;
        }
        return true;
    }

    private static void Apply(MenuFlyoutItem item, TaskMenuRow row, int iconSize)
    {
        item.Tag = row.Id;
        item.Text = row.Label;
        item.IsEnabled = row.Enabled;
        item.Icon = Icon(row, iconSize);
        AutomationProperties.SetName(item, row.Label);
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
