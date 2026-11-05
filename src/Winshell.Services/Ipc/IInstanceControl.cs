namespace Winshell;

/// <summary>What the control pipe does to the shell it serves. Every member runs on the shell's thread.</summary>
internal interface IInstanceControl
{
    /// <summary>The shell's IPC targets now. A reload can replace them.</summary>
    ShellIpc Ipc { get; }

    /// <summary>
    /// Reloads the shell. <paramref name="program"/> is the build to start when the running one kept its folder, and
    /// null starts this program again. Null when it worked, or else why not, for the client.
    /// </summary>
    string? Reload(string? program);

    /// <summary>Runs after a client has the answer to a reload that worked, which a shell that goes on to end has to wait for.</summary>
    void Reloaded()
    {
    }

    /// <summary>Ends the shell.</summary>
    void Quit();

    /// <summary>
    /// Shows the notice for a configuration that did not build, and the shell keeps running. An empty <paramref name="error"/>
    /// says the project built: that closes the build-error notice of <paramref name="projectFile"/>, and no other.
    /// </summary>
    void ShowBuildError(string error, string projectFile)
    {
    }

    /// <summary>What <c>winshell inspect</c> asked of the shell, and the text it prints. <see cref="IpcException"/> is a failed request.</summary>
    Task<string> Inspect(string command, IReadOnlyList<string> args)
    {
        throw new IpcException("This shell can't be inspected.");
    }

    /// <summary>A failed request as the client reads it.</summary>
    string Describe(Exception e)
    {
        return $"ipc: {e.GetType().Name}: {e.Message}";
    }
}
