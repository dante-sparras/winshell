namespace Winshell.Winland;

/// <summary>Where Winland keeps its config and its log. Its pipe does not report them, so they are found as Winland finds them.</summary>
internal static class WinlandFiles
{
    /// <summary>Names Winland's state file in place of <c>state.json</c> in <c>.winland</c>.</summary>
    public const string StateVariable = "WINLAND_STATE";

    public static string Config(string userProfile) => Path.Combine(userProfile, ".winland", "winland.conf");

    /// <summary><c>winland.log</c> in the folder of the state file.</summary>
    public static string Log(string userProfile, string? stateFile) =>
        Path.Combine(
            string.IsNullOrWhiteSpace(stateFile) ? Path.Combine(userProfile, ".winland") : Path.GetDirectoryName(Path.GetFullPath(stateFile)) ?? ".",
            "winland.log");
}
