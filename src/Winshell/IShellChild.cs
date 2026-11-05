namespace Winshell;

/// <summary>A shell process <see cref="ShellSupervisor"/> started. <see cref="ShellProcess"/> is the real one.</summary>
internal interface IShellChild : IDisposable
{
    /// <summary>Completes with the exit code once the process has ended.</summary>
    Task<int> Exited { get; }

    /// <summary>Whether <c>winshell kill</c> or <c>winshell reload</c> asked this shell to stop.</summary>
    bool StopAsked { get; }

    /// <summary>Whether <c>winshell reload</c> asked this shell to quit so that its supervisor starts it again.</summary>
    bool ReloadAsked { get; }

    /// <summary>Asks the shell to quit, as <c>winshell kill</c> does, and ends the process when it does not.</summary>
    Task StopAsync();
}
