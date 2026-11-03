using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>One diagnostics check: its result, and the fix under it when there is one.</summary>
public sealed partial class SettingsCheckRow : UserControl
{
    public SettingsCheckRow() => InitializeComponent();

    public void Bind(SettingsDiagnostic check)
    {
        Title.Text = check.Title;
        Message.Text = check.Message;
        Fix.Text = "Fix: " + check.Fix;
        Fix.Visibility = check.Fix.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Pass.Visibility = check.Status == "pass" ? Visibility.Visible : Visibility.Collapsed;
        Warn.Visibility = check.Status == "warn" ? Visibility.Visible : Visibility.Collapsed;
        Fail.Visibility = check.Status is not ("pass" or "warn") ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, SettingsAbout.Spoken(check));
    }
}
