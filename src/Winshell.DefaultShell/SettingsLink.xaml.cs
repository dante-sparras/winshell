using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>A setting that opens a page of Windows Settings. The card is the link.</summary>
public sealed partial class SettingsLink : HyperlinkButton
{
    public SettingsLink() => InitializeComponent();

    public SettingsCard Card => Body;

    public void Apply(SettingsEntry entry)
    {
        Body.Apply(entry, clickable: true, chevron: "\uE8A7");
        if (entry.Uri.Length > 0)
            NavigateUri = new Uri(entry.Uri);
        AutomationProperties.SetName(this, entry.Title);
        AutomationProperties.SetAccessibilityView(Body, AccessibilityView.Raw);
    }
}
