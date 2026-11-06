using Windows.ApplicationModel;
using Microsoft.Win32;

namespace Winshell.Session;

/// <summary>
/// The sign-in entry as Windows has it, read without changing anything: an unpackaged Winshell's value under the
/// user's <c>Run</c> key, the on and off Startup apps keeps for it, and a packaged Winshell's startup task.
/// <see cref="WindowsStartupRegistry"/> and <see cref="WindowsStartupTasks"/> build on these reads, and the doctor
/// calls them as they are. A registry read can throw; the caller decides what that means.
/// </summary>
internal static class SignInEntry
{
    /// <summary>The name of the <c>Run</c> value, and the package's startup task id.</summary>
    public const string Name = "Winshell";

    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where the Startup apps page keeps its on and off, one binary value per Run value.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>The command the <c>Run</c> value starts, or null when there is none.</summary>
    public static string? RegisteredCommand(string runKey = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey);
        return key?.GetValue(Name) as string;
    }

    /// <summary>Whether the Startup apps page turned the <c>Run</c> value off.</summary>
    public static bool TurnedOffInWindows(string approvedKey = ApprovedKey)
    {
        // The page writes 12 bytes: an odd first byte is off, with the time it was turned off after it.
        using var key = Registry.CurrentUser.OpenSubKey(approvedKey);
        return key?.GetValue(Name) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }

    /// <summary>
    /// The package's startup task, or null when the package has none or Windows does not hand it over. Only a
    /// packaged process calls this, and that call has no test: it would need an installed package.
    /// </summary>
    public static StartupTask? OpenTask()
    {
        try
        {
            // Sync because StartupService reads the task as it is made, and a UI binds its properties.
#pragma warning disable VSTHRD002 // Task.Run leaves the caller, so its context is not what it waits for.
            return Task.Run(static async () => await StartupTask.GetAsync(Name).AsTask().ConfigureAwait(false))
                .GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }
}
