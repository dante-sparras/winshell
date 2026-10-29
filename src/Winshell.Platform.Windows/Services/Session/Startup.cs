using Microsoft.Win32;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Session;

/// <summary>
/// The <c>Startup</c>: whether Winshell runs when the current user signs in. It is a <c>Winshell</c> value under the
/// user's <c>Run</c> key, which Windows' Startup apps page lists and can turn off. Nothing is registered until the
/// shell sets <see cref="RunAtSignIn"/>, and Winshell never changes what that page chose.
/// </summary>
public sealed class StartupService : QObject
{
    private readonly IStartupRegistry? _registry;
    private bool _registered;
    private bool _turnedOff;

    internal StartupService(QmlEngine engine)
    {
        _registry = engine.GetService<IStartupRegistry>();
        Read();
    }

    /// <summary>Whether this shell can register itself: false when it does not run as <c>winshell.exe</c>.</summary>
    [Qml] public bool Available { get { Track(); return _registry is not null; } }

    /// <summary>Whether Winshell is registered to run at sign-in. Setting it adds or removes the entry.</summary>
    [Qml]
    public bool RunAtSignIn
    {
        get { Track(); return _registered; }
        set
        {
            if (_registry is null)
            {
                ServiceSupport.Warn(this, "startup", "Startup.runAtSignIn needs a shell started by winshell.exe.");
                return;
            }
            if (value == _registered)
                return;
            if (!(value ? _registry.Register() : _registry.Unregister()))
                ServiceSupport.Warn(this, "startup", value ? "Windows did not let Winshell register to run at sign-in." : "Windows did not let Winshell stop running at sign-in.");
            Read();
        }
    }

    /// <summary>
    /// True when the entry is there but Windows' Startup apps page turned it off. Only that page can turn it back on.
    /// </summary>
    [Qml] public bool TurnedOffInWindows { get { Track(); return _turnedOff; } }

    /// <summary>Reads the entry again, after the user may have changed it in Windows.</summary>
    [Qml]
    public void Refresh() => Read();

    private void Read()
    {
        var registered = _registry?.IsRegistered() ?? false;
        var turnedOff = registered && (_registry?.TurnedOffInWindows() ?? false);
        if (SetField(ref _registered, registered, "runAtSignIn")) { }
        if (SetField(ref _turnedOff, turnedOff, "turnedOffInWindows")) { }
    }
}

/// <summary>The sign-in entry. Tests use a fake, or a key of their own, and never touch the user's Run key.</summary>
internal interface IStartupRegistry
{
    bool IsRegistered();

    bool TurnedOffInWindows();

    bool Register();

    bool Unregister();
}

/// <summary>A value under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, for the current user only.</summary>
internal sealed class WindowsStartupRegistry(string command, string runKey = WindowsStartupRegistry.RunKey, string approvedKey = WindowsStartupRegistry.ApprovedKey) : IStartupRegistry
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Where the Startup apps page keeps its on and off, one binary value per Run value.</summary>
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string ValueName = "Winshell";

    public string Command => command;

    public bool IsRegistered() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey);
        return key?.GetValue(ValueName) is string;
    });

    // The page writes 12 bytes: an odd first byte is off, with the time it was turned off after it.
    public bool TurnedOffInWindows() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(approvedKey);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    });

    public bool Register() => Try(() =>
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKey);
        key.SetValue(ValueName, command, RegistryValueKind.String);
        return true;
    });

    public bool Unregister() => Try(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        return true;
    });

    private static bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The command a sign-in runs: this <c>winshell.exe</c> with the arguments it was started with, under a console
    /// host that has no window. <c>winshell.exe</c> is a console program, so started on its own it would open a
    /// console or Windows Terminal at every sign-in. <c>--no-duplicate</c> keeps a second sign-in entry from starting
    /// a second shell, and <c>--daemonize</c> is left out because the console host already runs it in the background.
    /// A <c>--path</c> is made absolute, since a sign-in does not start in this working directory.
    /// </summary>
    public static string CommandFor(string exe, IEnumerable<string> args)
    {
        var kept = new List<string>();
        var path = false;
        foreach (var a in args)
        {
            if (a is "-d" or "--daemonize")
                continue;
            kept.Add(path ? Path.GetFullPath(a) : a);
            path = a is "-p" or "--path";
        }
        if (!kept.Any(a => a is "-n" or "--no-duplicate"))
            kept.Add("--no-duplicate");
        var conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        return string.Join(' ', new[] { conhost, "--headless", exe }.Concat(kept).Select(Quote));
    }

    /// <summary>Quotes an argument the way <c>CommandLineToArgvW</c> reads it back.</summary>
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => c is ' ' or '\t' or '"'))
            return arg;
        var sb = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }
}
