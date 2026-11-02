using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Winshell.Updates;

namespace Winshell.DefaultShell;

/// <summary>
/// The version, Winshell's own updates, the config folder, this instance's log, and the diagnostics card. The card
/// checks the configuration project. Those checks read the project file and run off the UI thread.
/// </summary>
public sealed partial class SettingsAboutPage : UserControl
{
    private readonly Preferences _preferences;
    private readonly Shell _shell;
    private readonly SettingsDiagnosticsSession _diagnostics;
    private readonly string _diagnosticsIdle;
    private bool _applying;

    public SettingsAboutPage(Preferences preferences, Shell shell)
    {
        _preferences = preferences;
        _shell = shell;
        _diagnostics = new(() => SettingsAbout.Checks(shell.ConfigRoot));
        InitializeComponent();
        var version = SettingsCatalog.RequireSetting("version");
        var updates = SettingsCatalog.RequireSetting("winshellUpdates");
        var automatic = SettingsCatalog.RequireSetting("autoUpdate");
        var folder = SettingsCatalog.RequireSetting("configFolder");
        var log = SettingsCatalog.RequireSetting("log");
        var diagnostics = SettingsCatalog.RequireSetting("diagnostics");
        _diagnosticsIdle = diagnostics.Description;
        VersionCard.Apply(version);
        UpdatesCard.Apply(updates);
        AutomaticCard.Apply(automatic);
        FolderCard.Apply(folder);
        LogCard.Apply(log);
        DiagnosticsCard.Apply(diagnostics);
        AutomationProperties.SetName(Automatic, automatic.Title);
        preferences.PropertyChanged += OnPreferences;
        shell.Updates.PropertyChanged += OnUpdates;
        _diagnostics.Updated += (_, _) => Show();
        Show();
    }

    public bool FocusSetting(string key)
    {
        var control = key switch
        {
            "version" => (Control)Copy,
            "winshellUpdates" => Update.Visibility == Visibility.Visible ? Update : UpdatesCard,
            "autoUpdate" => Automatic,
            "configFolder" => OpenFolder,
            "log" => OpenLog,
            "diagnostics" => RunDiagnostics,
            _ => null,
        };
        if (control is null)
            return false;
        control.Focus(FocusState.Programmatic);
        control.StartBringIntoView();
        return true;
    }

    private void OnPreferences(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(Preferences.AutoUpdate))
            Show();
    }

    private void OnUpdates(object? sender, PropertyChangedEventArgs e) => Show();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(SettingsAbout.CopyText(ProductVersion.Current.Text));
            Clipboard.SetContent(package);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _shell.Log.Warn("settings", $"Could not copy the version: {ex.Message}");
        }
    }

    private void OnUpdate(object sender, RoutedEventArgs e)
    {
        var updates = _shell.Updates;
        if (updates.Status == UpdateStatus.Ready)
            updates.Restart();
        else
            updates.Check();
    }

    private void OnAutomaticToggled(object sender, RoutedEventArgs e)
    {
        if (_applying)
            return;
        _preferences.AutoUpdate = Automatic.IsOn;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_shell.ConfigRoot.Length == 0)
            return;
        _preferences.Save();
        Open(_shell.ConfigRoot);
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (_shell.LogPath.Length == 0)
            return;
        Open(_shell.LogPath);
    }

    private void OnDiagnostics(object sender, RoutedEventArgs e) => _diagnostics.Run(_shell.Loop.Post);

    private void Show()
    {
        var updates = _shell.Updates;
        VersionCard.Description = ProductVersion.Current.Text;
        UpdatesCard.Description = SettingsAbout.Updates(updates.Available, updates.Winget, updates.Packaged, updates.Status, updates.Version, updates.Error);
        Update.Content = SettingsAbout.UpdateButton(updates.Status);
        Update.Visibility = updates.Available ? Visibility.Visible : Visibility.Collapsed;
        UpdatesCard.IsTabStop = !updates.Available;
        Update.IsEnabled = !SettingsAbout.UpdatesBusy(updates.Status);
        AutomationProperties.SetName(Update, SettingsAbout.UpdateButtonName(updates.Status));
        Automatic.IsEnabled = SettingsAbout.AutomaticEnabled(updates.Available);
        FolderCard.Description = SettingsAbout.Folder(_shell.ConfigRoot);
        OpenFolder.IsEnabled = _shell.ConfigRoot.Length > 0;
        LogCard.Description = SettingsAbout.Log(_shell.LogPath);
        OpenLog.IsEnabled = _shell.LogPath.Length > 0;
        DiagnosticsCard.Description = SettingsAbout.Diagnostics(_diagnostics.Available, _diagnostics.Running, _diagnostics.Summary, _diagnosticsIdle);
        RunDiagnostics.IsEnabled = SettingsAbout.DiagnosticsEnabled(_diagnostics.Available, _diagnostics.Running);
        _applying = true;
        Automatic.IsOn = SettingsAbout.AutomaticOn(updates.Available, _preferences.AutoUpdate);
        _applying = false;
        ShowChecks();
    }

    private void ShowChecks()
    {
        Checks.Children.Clear();
        foreach (var check in _diagnostics.Checks)
        {
            var row = new SettingsCheckRow();
            row.Bind(check);
            Checks.Children.Add(row);
        }
        Results.Visibility = _diagnostics.Checks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Open(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            _shell.Log.Warn("settings", $"Could not open \"{path}\": {e.Message}");
        }
    }
}
