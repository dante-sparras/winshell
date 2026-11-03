using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell;

/// <summary>One row of an order list: its name, and move up, move down and remove.</summary>
public sealed partial class SettingsOrderRow : UserControl
{
    private string _icon = "";

    public SettingsOrderRow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public string Key { get; private set; } = "";

    public Button UpButton => Up;

    public Button DownButton => Down;

    public Button RemoveButton => Remove;

    public event EventHandler<int>? Move;

    public event EventHandler? Removed;

    public void Bind(SettingsOrderItem item, int index, int count, string removeLabel, string removeGlyph)
    {
        Key = item.Key;
        Label.Text = item.Title;
        GlyphIcon.Glyph = item.Glyph;
        GlyphIcon.Visibility = item.Glyph.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _icon = item.Icon;
        Icon.Visibility = Visibility.Collapsed;
        Up.IsEnabled = index > 0;
        Down.IsEnabled = index < count - 1;
        RemoveIcon.Glyph = removeGlyph;
        AutomationProperties.SetName(this, item.Title);
        AutomationProperties.SetPositionInSet(this, index + 1);
        AutomationProperties.SetSizeOfSet(this, count);
        AutomationProperties.SetName(Up, "Move " + item.Title + " up");
        AutomationProperties.SetName(Down, "Move " + item.Title + " down");
        AutomationProperties.SetName(Remove, removeLabel + " " + item.Title);
        ToolTipService.SetToolTip(Up, "Move up");
        ToolTipService.SetToolTip(Down, "Move down");
        ToolTipService.SetToolTip(Remove, removeLabel);
        if (IsLoaded)
            LoadIcon();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => LoadIcon();

    private void LoadIcon()
    {
        if (_icon.Length == 0)
        {
            Icon.Source = null;
            Icon.Visibility = Visibility.Collapsed;
            return;
        }
        Icon.Source = ShellIcon.Load(_icon, ShellIcon.Pixels(XamlRoot, 20));
        Icon.Visibility = Icon.Source is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnUp(object sender, RoutedEventArgs e) => Move?.Invoke(this, -1);

    private void OnDown(object sender, RoutedEventArgs e) => Move?.Invoke(this, 1);

    private void OnRemove(object sender, RoutedEventArgs e) => Removed?.Invoke(this, EventArgs.Empty);
}
