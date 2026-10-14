using System.Globalization;
using System.Management;

namespace Winshell;

/// <summary>Writes the shell's own lifetime word. A crash leaves <c>running</c>, so a watcher starts the shell again.</summary>
internal static class LifetimeFile
{
    public static void Write(ShellLife word)
    {
        var path = ShellLifetime.PathIn(ConfigFolder.Path);
        var text = ShellLifetime.Text(word);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder.Path);
                File.WriteAllText(path, text);
                return;
            }
            catch (IOException ex) when (ex.HResult is 32 or 33 && attempt < 4)
            {
                Thread.Sleep(40);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning("Lifetime", "could not write " + path, ex);
                return;
            }
        }
    }

    public static string? Read()
    {
        try
        {
            return File.ReadAllText(ShellLifetime.PathIn(ConfigFolder.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Starts a process through WMI, so it is not a child of this one.</summary>
internal static class ProcessStart
{
    public static bool Create(string exe, string arguments)
    {
        try
        {
            using var cls = new ManagementClass("Win32_Process");
            var input = cls.GetMethodParameters("Create");
            var command = "\"" + exe + "\"";
            if (arguments.Length > 0)
            {
                command += " " + arguments;
            }

            input["CommandLine"] = command;
            input["CurrentDirectory"] = Path.GetDirectoryName(exe);
            var output = cls.InvokeMethod("Create", input, null);
            var code = output is null ? -1 : Convert.ToInt32(output["ReturnValue"], CultureInfo.InvariantCulture);
            if (code != 0)
            {
                Log.Warning("Process", exe + " did not start; Win32_Process.Create returned " + code.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("Process", exe + " did not start", ex);
            return false;
        }
    }
}

/// <summary>Starts <c>Winland.Host.exe</c> when no window manager holds <c>Local\Winland</c>. The shell's lifetime word is not passed on.</summary>
internal static class WinlandStart
{
    public static void Ensure()
    {
        if (ShellWin32.WinlandHeld())
        {
            Log.Information("Winland", "already running");
            return;
        }

        var exe = Path.Combine(ProductFolders.Sibling(AppContext.BaseDirectory, ProductFolders.Winland), "Winland.Host.exe");
        if (!File.Exists(exe))
        {
            Log.Warning("Winland", "Winland.Host.exe is not in " + Path.GetDirectoryName(exe));
            return;
        }

        if (ProcessStart.Create(exe, ""))
        {
            Log.Information("Winland", "started");
        }
    }
}

/// <summary>
/// Waits for the shell and then does what its lifetime word asks.
/// The word is read once, as the shell left it. A shell that comes next writes <c>running</c> over that word,
/// and reading it again would look like a crash.
/// </summary>
internal static class ShellWatch
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int RestartLimit = 3;
    private const int ArrivalPollMs = 250;

    public static bool IsWatch(string[] args) =>
        args.Length >= 2
        && args[0] == "--watch"
        && int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out _);

    public static void Run(string[] args)
    {
        var pid = int.Parse(args[1], CultureInfo.InvariantCulture);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            var born = StartedAt(process);
            Log.Information("Watch", "waiting for process " + pid.ToString(CultureInfo.InvariantCulture));
            while (!WatchedProcess.Gone(born, Look(pid, born)))
            {
                Thread.Sleep(ArrivalPollMs);
            }

            Log.Information("Watch", "process " + pid.ToString(CultureInfo.InvariantCulture) + " exited");
        }
        catch (ArgumentException)
        {
            Log.Information("Watch", "watched process " + pid.ToString(CultureInfo.InvariantCulture) + " had already exited");
        }
        catch (Exception ex)
        {
            Log.Error("Watch", "could not wait for process " + pid.ToString(CultureInfo.InvariantCulture), ex);
        }

        // Once. The next shell writes running within milliseconds of taking the process.
        var word = ShellLifetime.Read(LifetimeFile.Read());
        var started = DateTime.UtcNow;
        ShellWin32.RestoreCursors();
        var step = ShellLifetime.AfterExit(word, ShellWin32.ShellHeld(), TimeSpan.Zero);
        if (step == ShellExit.Wait)
        {
            Log.Information("Watch", "process " + pid.ToString(CultureInfo.InvariantCulture) + " was replaced; waiting up to " + ((int)ShellLifetime.ReplaceLimit.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " seconds for the next shell");
            while (step == ShellExit.Wait)
            {
                Thread.Sleep(ArrivalPollMs);
                step = ShellLifetime.AfterExit(word, ShellWin32.ShellHeld(), DateTime.UtcNow - started);
            }
        }

        if (step == ShellExit.Leave)
        {
            Log.Information("Watch", "another shell holds Local\\Winshell");
            return;
        }

        if (step == ShellExit.Restart)
        {
            if (!BudgetAllows())
            {
                StayDown();
                Log.Warning("Watch", "restart skipped after 3 crashes in a minute");
                return;
            }

            NoteRestart();
            Restart(args);
            return;
        }

        StayDown();
        Log.Information("Watch", word == ShellLife.Replacing ? "no shell came to replace process " + pid.ToString(CultureInfo.InvariantCulture) : "leaving the shell down");
    }

    private static void StayDown()
    {
        ShellWin32.GiveBack();
        Program.GiveExtensionsBack();
    }

    private static void Restart(string[] args)
    {
        var exe = args.Length > 2 ? args[2] : "";
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = Path.Combine(AppContext.BaseDirectory, "Winshell.Host.exe");
        }

        var arguments = "";
        if (!string.Equals(ConfigFolder.Path, ConfigFolder.DefaultPath, StringComparison.OrdinalIgnoreCase))
        {
            arguments = ConfigFolder.ConfigSwitch + " \"" + ConfigFolder.Path + "\"";
        }

        if (!ProcessStart.Create(exe, arguments))
        {
            StayDown();
        }
    }

    private static DateTimeOffset? StartedAt(System.Diagnostics.Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static WatchedProcess.Sight? Look(int pid, DateTimeOffset? started)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            try
            {
                if (process.HasExited)
                {
                    return new WatchedProcess.Sight(started ?? default, true);
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            try
            {
                return new WatchedProcess.Sight(new DateTimeOffset(process.StartTime), false);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new WatchedProcess.Sight(started ?? default, false);
            }
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string RestartPath => Path.Combine(ConfigFolder.Path, "restarts");

    private static bool BudgetAllows() => RecentRestarts().Count < RestartLimit;

    private static void NoteRestart()
    {
        var recent = RecentRestarts();
        recent.Add(DateTimeOffset.Now);
        try
        {
            Directory.CreateDirectory(ConfigFolder.Path);
            File.WriteAllLines(RestartPath, recent.Select(stamp => stamp.ToString("o", CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Watch", "could not record the restart", ex);
        }
    }

    private static List<DateTimeOffset> RecentRestarts()
    {
        var cutoff = DateTimeOffset.Now - Window;
        try
        {
            if (!File.Exists(RestartPath))
            {
                return [];
            }

            return File.ReadAllLines(RestartPath)
                .Select(line => DateTimeOffset.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp) ? stamp : (DateTimeOffset?)null)
                .Where(stamp => stamp is not null && stamp > cutoff)
                .Select(stamp => stamp!.Value)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Watch", "could not read restarts", ex);
            return [];
        }
    }
}
