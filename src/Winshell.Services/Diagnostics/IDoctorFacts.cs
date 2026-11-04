namespace Winshell.Diagnostics;

/// <summary>
/// What <see cref="Doctor.Run"/> reads from this PC. Every read stays a read: the doctor never builds, and a
/// configuration project is judged from the project file alone.
/// </summary>
internal interface IDoctorFacts
{
    int WindowsBuild();

    int WindowsUpdateRevision();

    string? WindowsDisplayVersion();

    bool WindowsServer();

    bool TransparencyEffectsOff();

    bool ContrastThemeOn();

    bool SelfContained(string exePath);

    IReadOnlyList<string> UserConfigProblems(string? configFile, string configRoot, string localRoot);

    bool CanWrite(string folder);

    bool IsElevated(int pid);

    IReadOnlyList<DoctorScreens.MonitorFacts> Monitors();

    IReadOnlyList<DoctorScreens.WindowFacts> EdgeWindows();

    bool PerMonitorV2();

    IReadOnlyList<string> CompatibilityLayers(string exePath);

    string? RunEntry();

    bool RunEntryTurnedOff();

    string ResolveLinks(string path);

    string NotificationAccess();

    DoctorCrashes.CrashFolderRead ReadCrashReports(string folder);

    DoctorCrashes.CrashPeek PeekCrash(string path);
}
