namespace Winshell.Core;

/// <summary>The named pipe between <c>winshell.exe</c> and the running shell.</summary>
public static class ShellPipe
{
    /// <summary>The name without this Windows session's number.</summary>
    public const string Prefix = "Winshell.Ctl";

    /// <summary>How long a client waits for a reply, in milliseconds.</summary>
    public const int TimeoutMilliseconds = 2000;

    /// <summary>The pipe for this Windows session. Another session does not see it.</summary>
    public static string Name => Prefix + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
}
