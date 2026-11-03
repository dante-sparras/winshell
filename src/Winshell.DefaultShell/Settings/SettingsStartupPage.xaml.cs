using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

/// <summary>Whether Winshell runs when the user signs in. Windows' Startup apps page is a link away.</summary>
public sealed partial class SettingsStartupPage : UserControl
{
    private readonly Shell _shell;
    private bool _applying;

    public SettingsStartupPage(Shell shell)
    {
        _shell = shell;
        InitializeComponent();
        var signIn = SettingsCatalog.RequireSetting("runAtSignIn");
        SignInCard.Apply(signIn);
        StartupApps.Apply(SettingsCatalog.RequireSetting("startupApps"));
        AutomationProperties.SetName(SignIn, signIn.Title);
        shell.Startup.PropertyChanged += OnStartup;
        Show();
    }

    public static string Description(bool available, bool turnedOff, bool elsewhere) =>
        !available ? "Only a shell started by winshell.exe can run at sign-in"
        : turnedOff ? "Turned off in Startup apps in Windows, which can turn it back on"
        : elsewhere ? "Set to start Winshell from another folder or another way. Turn it on to start this one"
        : "For your account only";

    public static bool SwitchEnabled(bool available, bool turnedOff) => available && !turnedOff;

    public static bool SwitchChecked(bool runAtSignIn, bool turnedOff) => runAtSignIn && !turnedOff;

    public void RefreshFromWindows()
    {
        _shell.Startup.Refresh();
        Show();
    }

    public bool FocusSetting(string key)
    {
        var control = key switch
        {
            "runAtSignIn" => (Control)SignIn,
            "startupApps" => StartupApps,
            _ => null,
        };
        if (control is null)
            return false;
        control.Focus(FocusState.Programmatic);
        control.StartBringIntoView();
        return true;
    }

    private void OnStartup(object? sender, PropertyChangedEventArgs e) => Show();

    private void Show()
    {
        var startup = _shell.Startup;
        SignInCard.Description = Description(startup.Available, startup.TurnedOffInWindows, startup.RegisteredElsewhere);
        _applying = true;
        SignIn.IsEnabled = SwitchEnabled(startup.Available, startup.TurnedOffInWindows);
        SignIn.IsOn = SwitchChecked(startup.RunAtSignIn, startup.TurnedOffInWindows);
        _applying = false;
    }

    private void OnSignInToggled(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        _shell.Startup.RunAtSignIn = SignIn.IsOn;
        Show();
    }
}
