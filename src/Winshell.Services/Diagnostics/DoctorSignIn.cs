using Winshell.Session;
using StartupTaskState = Windows.ApplicationModel.StartupTaskState;

namespace Winshell.Diagnostics;

/// <summary>The doctor's check of the sign-in entry, or of a packaged Winshell's startup task.</summary>
internal static class DoctorSignIn
{
    /// <param name="command">The command line of the sign-in entry, or null when there is none.</param>
    /// <param name="turnedOff">Whether Startup apps in Windows turned the entry off.</param>
    /// <param name="thisExe">The path of the running <c>winshell.exe</c>.</param>
    /// <param name="configRoot">The config folder the entry's <c>-c</c>, or its lack of <c>-p</c> and <c>-c</c>, is read against.</param>
    /// <param name="exists">Whether a file exists.</param>
    /// <param name="resolve">The file a path names in the end, past links: a winget install starts through one.</param>
    /// <param name="projectFile">The project of the configuration being checked, or null when the check is not about one.</param>
    internal static DoctorCheck SignIn(string? command, bool turnedOff, string thisExe, string configRoot,
        Func<string, bool> exists, Func<string, string>? resolve = null, string? projectFile = null)
    {
        resolve ??= path => path;
        if (command is null)
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass,
                "Winshell does not start when you sign in. install.ps1 -AutoStart turns that on.");
        var launch = SignInEntry.Read(command);
        var exe = launch.Program;
        if (exe is null || !exists(exe))
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Fail,
                $"The sign-in entry starts {exe ?? command}, which is not there.",
                "Install Winshell again with -AutoStart.");
        // Start launches winshellw.exe. Sign-in runs the winshell.exe beside it. They are one install.
        if (!SignInEntry.SameInstall(exe, thisExe, resolve))
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                $"The sign-in entry starts another winshell.exe: {exe}.",
                "Install Winshell again with -AutoStart, so the entry starts the one you use.");
        if (projectFile is not null &&
            !SignInEntry.Starts(launch, projectFile, configRoot))
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                "The sign-in entry does not start this configuration.",
                "Turn on Run at sign-in for this configuration, so the entry starts it.");
        if (turnedOff)
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                "Winshell is set to start when you sign in, but Startup apps in Windows turned it off.",
                "Turn Winshell on in Settings > Apps > Startup.");
        return new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in");
    }

    /// <summary>Sign-in for a packaged Winshell: the startup task, which names no executable path.</summary>
    /// <param name="state">The startup task's state, or null when the package has none.</param>
    internal static DoctorCheck SignIn(StartupTaskState? state)
    {
        if (state is null)
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                "This package has no startup task, so Winshell cannot run when you sign in.");
        return state switch
        {
            StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy =>
                new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass, "Winshell starts when you sign in"),
            StartupTaskState.DisabledByUser =>
                new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                    "Winshell is set to start when you sign in, but Startup apps in Windows turned it off.",
                    "Turn Winshell on in Settings > Apps > Startup."),
            StartupTaskState.DisabledByPolicy =>
                new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                    "Winshell is set to start when you sign in, but Windows policy turned it off.",
                    "A Windows policy controls this. Startup apps in Settings cannot turn it on."),
            _ => new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass,
                "Winshell does not start when you sign in. Turn it on in Settings > Apps > Startup.")
        };
    }
}
