using System.Text.Json.Serialization;

namespace Winshell;

/// <summary>
/// The control pipe's ops: each one's name, the shape of its arguments, and whether it must reach a shell of another
/// release. The requests are built here and read here, so the CLI that sends one and the shell that handles it cannot
/// disagree. <c>call</c>, <c>show</c>, <c>prop</c>, <c>wait</c> and <c>listen</c> pass through what a user types, so
/// the module gives them a name and a request, and no more.
/// </summary>
internal static class IpcOps
{
    public const string Show = "show";
    public const string Call = "call";
    public const string Prop = "prop";
    public const string Wait = "wait";
    public const string Listen = "listen";
    public const string Kill = "kill";
    public const string Reload = "reload";
    public const string Build = "build";
    public const string Inspect = "inspect";

    /// <summary>
    /// Whether a shell and a winshell of different releases refuse <paramref name="op"/>. Kill is left out: an update can
    /// change winshell under a running shell, and it must still be possible to stop it.
    /// </summary>
    public static bool ChecksRelease(string op) => op != Kill;
}

internal sealed partial record IpcRequest
{
    public static IpcRequest ForShow(bool json) => new(IpcOps.Show, Json: json);

    public static IpcRequest ForCall(string target, string name, List<string> args) => new(IpcOps.Call, target, name, args);

    public static IpcRequest ForProp(string target, string name) => new(IpcOps.Prop, target, name);

    /// <summary>Answers once, with the first signal.</summary>
    public static IpcRequest ForWait(string target, string name) => new(IpcOps.Wait, target, name);

    /// <summary>Answers with each signal until the shell quits.</summary>
    public static IpcRequest ForListen(string target, string name) => new(IpcOps.Listen, target, name);

    public static IpcRequest ForKill() => new(IpcOps.Kill);

    /// <summary>A reload. A <paramref name="program"/> is the one a handover starts, and none keeps the program this shell runs.</summary>
    public static IpcRequest ForReload(string? program = null) => new(IpcOps.Reload, Args: program is null ? null : [program]);

    /// <summary>The program a reload hands over to, or null.</summary>
    [JsonIgnore]
    public string? ReloadProgram => Args is { Count: > 0 } args ? args[0] : null;

    /// <summary>
    /// What a build came to. An empty <paramref name="error"/> says the project built, which closes that project's
    /// build-error notice.
    /// </summary>
    public static IpcRequest ForBuild(string error, string projectFile) => new(IpcOps.Build, Args: [error, projectFile]);

    /// <summary>The error and the project file of a build request, or null when it carries something else.</summary>
    [JsonIgnore]
    public (string Error, string ProjectFile)? BuildReport => Args is { Count: 2 } args ? (args[0], args[1]) : null;

    public static IpcRequest ForInspect(string command, List<string> args) => new(IpcOps.Inspect, command, Args: args);
}
