using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>A setting that opens a page of Winshell Settings. The card is the button.</summary>
public sealed partial class SettingsCommand : Button
{
    public SettingsCommand() => InitializeComponent();

    public void Apply(SettingsEntry entry, string? description = null)
    {
        Body.Apply(entry, description, clickable: true, chevron: "\uE76C");
        AutomationProperties.SetName(this, entry.Title);
        AutomationProperties.SetAutomationId(this, entry.Key);
        AutomationProperties.SetAccessibilityView(Body, AccessibilityView.Raw);
    }
}
