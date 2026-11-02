using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>Win+X. The rows are the ones <see cref="DesktopSwitcher.Commands"/> names. It does not shut down or open Task Manager.</summary>
public sealed partial class PowerMenuView : UserControl
{
    private readonly List<Button> _buttons = [];

    public PowerMenuView(Action<PowerCommand> run)
    {
        InitializeComponent();
        foreach (var command in DesktopSwitcher.Commands)
        {
            var row = command;
            var root = new Grid();
            var hover = new Border { Style = (Style)Resources["HoverFill"], Visibility = Visibility.Collapsed };
            var button = new Button { Style = (Style)Resources["CommandButton"], Content = row.Name };
            AutomationProperties.SetName(button, row.Name);
            button.Click += (_, _) => run(row);
            button.PointerEntered += (_, _) => hover.Visibility = Visibility.Visible;
            button.PointerExited += (_, _) => hover.Visibility = Visibility.Collapsed;
            button.PointerCanceled += (_, _) => hover.Visibility = Visibility.Collapsed;
            root.Children.Add(hover);
            root.Children.Add(button);
            Rows.Children.Add(root);
            _buttons.Add(button);
        }
    }

    /// <summary>The first row, which is where the menu opens.</summary>
    public void FocusFirst()
    {
        if (_buttons.Count > 0)
            _buttons[0].Focus(FocusState.Keyboard);
    }
}
