namespace Winshell.Platform.Windows.Diagnostics;

/// <summary>
/// What <see cref="Doctor.Run"/> reads on this PC. Each call only reads. A configuration project is not one of these:
/// <see cref="Doctor.Configuration"/> reads the project file itself.
/// </summary>
public sealed class DoctorFacts : IDoctorFacts
{
    /// <summary>The reads the QML stack's doctor makes.</summary>
    public static readonly DoctorFacts Pc = new();

    private DoctorFacts()
    {
    }

    public (string? LoadError, string? Unreadable) ReadInstanceLog(string logPath)
    {
        var read = DoctorProbe.ReadInstanceLog(logPath);
        return (read.LoadError, read.Unreadable);
    }

    public int WindowsBuild() => DoctorProbe.WindowsBuild();

    public int WindowsUpdateRevision() => DoctorProbe.WindowsUpdateRevision();

    public string? WindowsDisplayVersion() => DoctorProbe.WindowsDisplayVersion();

    public bool WindowsServer() => DoctorProbe.WindowsServer();

    public bool TransparencyEffectsOff() => DoctorProbe.TransparencyEffectsOff();

    public bool ContrastThemeOn() => DoctorProbe.ContrastThemeOn();

    public bool SelfContained(string exePath) => DoctorProbe.SelfContained(exePath);

    public Doctor.ConfigScan ParseConfig(string? rootFile) => DoctorProbe.ParseConfig(rootFile);

    public IReadOnlyList<string> UserConfigProblems(string? configFile, string configRoot, string localRoot) =>
        DoctorProbe.UserConfigProblems(configFile, configRoot, localRoot);

    public bool CanWrite(string folder) => DoctorProbe.CanWrite(folder);

    public bool IsElevated(int pid) => DoctorProbe.IsElevated(pid);

    public IReadOnlyList<Doctor.MonitorFacts> Monitors() => DoctorProbe.Monitors();

    public IReadOnlyList<Doctor.WindowFacts> EdgeWindows() => DoctorProbe.EdgeWindows();

    public bool PerMonitorV2() => DoctorProbe.PerMonitorV2();

    public IReadOnlyList<string> CompatibilityLayers(string exePath) => DoctorProbe.CompatibilityLayers(exePath);

    public string? RunEntry() => DoctorProbe.RunEntry();

    public bool RunEntryTurnedOff() => DoctorProbe.RunEntryTurnedOff();

    public string ResolveLinks(string path) => DoctorProbe.ResolveLinks(path);

    public string NotificationAccess() => DoctorProbe.NotificationAccess();

    public Doctor.CrashFolderRead ReadCrashReports(string folder) => DoctorProbe.ReadCrashReports(folder);

    public Doctor.CrashPeek PeekCrash(string path) => DoctorProbe.PeekCrash(path);
}
