using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>A page whose controls arrive in the next part of Winshell Settings. Search and open still land here.</summary>
public sealed partial class SettingsStubPage : UserControl
{
    public SettingsStubPage(string title)
    {
        Title = title;
        InitializeComponent();
        AutomationProperties.SetName(this, title);
    }

    public string Title { get; }

    public bool FocusSetting(string key) => false;
}
