namespace Winshell.Services.Tests;

/// <summary>A shell behind the control pipe that only counts what it was asked.</summary>
internal sealed class FakeInstanceControl : IInstanceControl
{
    public string? ReloadError { get; init; }

    /// <summary>What <see cref="Inspect"/> throws, or null to answer with the command.</summary>
    public Exception? InspectFailure { get; init; }

    public int Reloads { get; private set; }

    public int Answered { get; private set; }

    public int Quits { get; private set; }

    public string? BuildError { get; private set; }

    public string? BuildProject { get; private set; }

    public ShellIpc Ipc { get; } = new(new Log());

    public string? Reload(string? program)
    {
        Reloads++;
        return ReloadError;
    }

    public void Reloaded()
    {
        Answered++;
    }

    public void Quit()
    {
        Quits++;
    }

    public void ShowBuildError(string error, string projectFile)
    {
        BuildError = error;
        BuildProject = projectFile;
    }

    public void DismissNotice()
    {
    }

    public Task<string> Inspect(string command, IReadOnlyList<string> args)
    {
        return InspectFailure is null ? Task.FromResult(command) : Task.FromException<string>(InspectFailure);
    }
}
