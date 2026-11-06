using Winshell.Native;
using StartupTaskState = Windows.ApplicationModel.StartupTaskState;

namespace Winshell.Diagnostics;

/// <summary>The doctor's check of the sign-in entry, or of a packaged Winshell's startup task.</summary>
internal static class DoctorSignIn
{
    /// <summary>The <c>winshell.exe</c> a sign-in command starts: after <c>conhost.exe --headless</c>, as Winshell writes it, or first.</summary>
    internal static string? ExeOf(string command)
    {
        var args = CommandLine.Split(command);
        if (args.Length >= 3 && Path.GetFileName(args[0]).Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) &&
            args[1] == "--headless")
            return args[2];
        return args.Length > 0 ? args[0] : null;
    }

    /// <param name="command">The command line of the sign-in entry, or null when there is none.</param>
    /// <param name="turnedOff">Whether Startup apps in Windows turned the entry off.</param>
    /// <param name="thisExe">The path of the running <c>winshell.exe</c>.</param>
    /// <param name="exists">Whether a file exists.</param>
    /// <param name="resolve">The file a path names in the end, past links: a winget install starts through one.</param>
    internal static DoctorCheck SignIn(string? command, bool turnedOff, string thisExe, Func<string, bool> exists,
        Func<string, string>? resolve = null)
    {
        resolve ??= path => path;
        if (command is null)
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Pass,
                "Winshell does not start when you sign in. install.ps1 -AutoStart turns that on.");
        var exe = ExeOf(command);
        if (exe is null || !exists(exe))
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Fail,
                $"The sign-in entry starts {exe ?? command}, which is not there.",
                "Install Winshell again with -AutoStart.");
        var started = resolve(Path.GetFullPath(exe));
        var here = resolve(Path.GetFullPath(thisExe));
        // Start launches winshellw.exe. Sign-in runs the winshell.exe beside it. They are one install.
        if (!string.Equals(started, here, StringComparison.OrdinalIgnoreCase) && !SameInstall(started, here))
            return new DoctorCheck("signIn", "Sign-in", CheckStatus.Warn,
                $"The sign-in entry starts another winshell.exe: {exe}.",
                "Install Winshell again with -AutoStart, so the entry starts the one you use.");
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

    /// <summary><c>winshell.exe</c> and <c>winshellw.exe</c> in the same folder are one install.</summary>
    private static bool SameInstall(string left, string right)
    {
        static bool Pair(string path)
        {
            var name = Path.GetFileName(path);
            return name.Equals("winshell.exe", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("winshellw.exe", StringComparison.OrdinalIgnoreCase);
        }

        if (!Pair(left) || !Pair(right))
            return false;
        return string.Equals(Path.GetDirectoryName(Path.GetFullPath(left)),
            Path.GetDirectoryName(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    }
}
