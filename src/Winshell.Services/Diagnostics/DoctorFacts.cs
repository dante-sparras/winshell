namespace Winshell.Diagnostics;

/// <summary>
/// What <see cref="Doctor.Run"/> reads on this PC. Each call only reads. A configuration project is not one of these:
/// <see cref="Doctor.Configuration"/> reads the project file itself.
/// </summary>
internal sealed class DoctorFacts : IDoctorFacts
{
    /// <summary>The reads <c>winshell doctor</c> and Settings' About page make.</summary>
    public static readonly DoctorFacts Pc = new();

    private DoctorFacts()
    {
    }

    public int WindowsBuild() => DoctorProbe.WindowsBuild();

    public int WindowsUpdateRevision() => DoctorProbe.WindowsUpdateRevision();

    public string? WindowsDisplayVersion() => DoctorProbe.WindowsDisplayVersion();

    public bool WindowsServer() => DoctorProbe.WindowsServer();

    public bool TransparencyEffectsOff() => DoctorProbe.TransparencyEffectsOff();

    public bool ContrastThemeOn() => DoctorProbe.ContrastThemeOn();

    public bool SelfContained(string exePath) => DoctorProbe.SelfContained(exePath);

    public IReadOnlyList<string> UserConfigProblems(string? configFile, string configRoot, string localRoot) =>
        DoctorProbe.UserConfigProblems(configFile, configRoot, localRoot);

    public bool CanWrite(string folder) => DoctorProbe.CanWrite(folder);

    public bool IsElevated(int pid) => DoctorProbe.IsElevated(pid);

    public IReadOnlyList<DoctorScreens.MonitorFacts> Monitors() => DoctorProbe.Monitors();

    public IReadOnlyList<DoctorScreens.WindowFacts> EdgeWindows() => DoctorProbe.EdgeWindows();

    public bool PerMonitorV2() => DoctorProbe.PerMonitorV2();

    public IReadOnlyList<string> CompatibilityLayers(string exePath) => DoctorProbe.CompatibilityLayers(exePath);

    public string? RunEntry() => DoctorProbe.RunEntry();

    public bool RunEntryTurnedOff() => DoctorProbe.RunEntryTurnedOff();

    public string ResolveLinks(string path) => DoctorProbe.ResolveLinks(path);

    public string NotificationAccess() => DoctorProbe.NotificationAccess();

    public Doctor.CrashFolderRead ReadCrashReports(string folder) => DoctorProbe.ReadCrashReports(folder);

    public Doctor.CrashPeek PeekCrash(string path) => DoctorProbe.PeekCrash(path);
}
