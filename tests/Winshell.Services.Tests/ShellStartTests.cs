using System.Diagnostics;

namespace Winshell.Services.Tests;

/// <summary>What a start reads before it is a shell: its command line, its id, and its folders.</summary>
public sealed class ShellStartTests
{
    private static readonly ShellLocations Locations = new(@"C:\config", @"C:\local");

    [Fact]
    public void After_and_no_duplicate_are_read_and_the_rest_stays_the_programs()
    {
        var arguments = ShellArguments.Parse(["--theme", "dark", "--after", "4242", "-n"]);

        Assert.Equal(4242, arguments.After);
        Assert.True(arguments.NoDuplicate);
        Assert.Equal(["--theme", "dark", "-n"], arguments.Kept);
    }

    [Theory]
    [InlineData("--after")]
    [InlineData("--after soon")]
    [InlineData("-- --after 12 -n")]
    public void Without_a_process_id_or_after_a_double_dash_nothing_is_taken(string line)
    {
        var given = line.Split(' ');

        var arguments = ShellArguments.Parse(given);

        Assert.Null(arguments.After);
        Assert.False(arguments.NoDuplicate);
        Assert.Equal(given, arguments.Kept);
    }

    [Fact]
    public void The_next_process_keeps_the_arguments_and_names_only_this_one()
    {
        var arguments = ShellArguments.Parse(["--after", "1", "--no-duplicate", "--", "a"]);

        Assert.Equal(["--after", "2", "--no-duplicate", "--", "a"], arguments.ForNext(2));
        Assert.Equal(2, ShellArguments.Parse(arguments.ForNext(2)).After);
        Assert.False(ShellArguments.Parse(arguments.ForNext(2)).Daemonize);
    }

    [Fact]
    public void A_detached_start_drops_d_and_keeps_the_handoff()
    {
        var arguments = ShellArguments.Parse(["-d", "--after", "7", "--theme", "dark", "--daemonize", "--", "-d"]);

        Assert.True(arguments.Daemonize);
        Assert.Equal(7, arguments.After);
        Assert.Equal(["--theme", "dark", "--", "-d"], arguments.Kept);
        Assert.Equal(["--after", "7", "--theme", "dark", "--", "-d"], arguments.Detached);
        Assert.Equal(["--after", "8", "--theme", "dark", "--", "-d"], arguments.ForNext(8));
        Assert.False(ShellArguments.Parse(arguments.ForNext(8)).Daemonize);

        var start = InstanceStart.DetachedStart(@"C:\shells\winshell-next.exe", arguments.Detached);
        Assert.Equal(@"C:\shells\winshell-next.exe", start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Normal, start.WindowStyle);
        Assert.Equal(["--after", "7", "--theme", "dark", "--", "-d"], start.ArgumentList);
    }

    [Fact]
    public void A_given_shell_id_names_the_folders()
    {
        var identity = ShellIdentity.Resolve(@"C:\shells\mine\mine.exe", Locations, "mine-id", null, null, null);

        Assert.Equal("mine-id", identity.ShellId);
        Assert.Equal(@"C:\local\data\by-shell\mine-id", identity.DataDir);
        Assert.Equal(@"C:\local\state\by-shell\mine-id", identity.StateDir);
        Assert.Equal(@"C:\local\cache\by-shell\mine-id", identity.CacheDir);
    }

    [Theory]
    [InlineData(@"C:\config\bin\shell\Mine.exe", "root")]
    [InlineData(@"C:\config\Bar\bin\shell\Bar.exe", "bar")]
    [InlineData(@"C:\config\Bar\bin\shell.next\Bar.exe", "bar")]
    public void A_configuration_in_the_config_folder_is_named_after_its_folder(string program, string id)
    {
        Assert.Equal(id, IdOf(program));
    }

    [Fact]
    public void A_configuration_anywhere_else_is_named_after_its_path_in_any_case_from_either_build_folder()
    {
        var one = IdOf(@"C:\shells\mine\bin\shell\Mine.exe");

        // The id an earlier Winshell gave it, so its folders stay where they are.
        Assert.Equal("cea0f30d0e45e5a6bf33f1d21c3e6351", one);
        Assert.Equal(one, IdOf(@"C:\shells\mine\bin\shell.next\Mine.exe"));
        Assert.Equal(one, IdOf(@"C:\SHELLS\MINE\BIN\SHELL\MINE.EXE"));
        Assert.NotEqual(one, IdOf(@"C:\shells\yours\bin\shell\Yours.exe"));
    }

    [Fact]
    public void Folders_named_after_the_earlier_hash_move_to_the_configurations_name_once()
    {
        using var temp = new TempFolder();
        var locations = new ShellLocations(Path.Combine(temp.Path, "config"), Path.Combine(temp.Path, "local"));
        var program = Path.Combine(locations.ConfigRoot, "bar", "bin", "shell", "Bar.exe");
        // Before a configuration was named after its folder, its program's path named it, as it does with no config folder.
        var earlier = ShellIdentity.Resolve(program, locations with { ConfigRoot = "" }, null, null, null, null);
        Directory.CreateDirectory(earlier.DataDir);
        File.WriteAllText(Path.Combine(earlier.DataDir, "kept.txt"), "data");
        Directory.CreateDirectory(earlier.StateDir);
        var messages = new List<string>();
        var log = new Log { Sink = (_, category, message) => messages.Add($"{category}: {message}") };

        var identity = ShellIdentity.Resolve(program, locations, null, null, null, null);
        identity.CarryOver(log);

        Assert.Equal("data", File.ReadAllText(Path.Combine(identity.DataDir, "kept.txt")));
        Assert.True(Directory.Exists(identity.StateDir));
        Assert.False(Directory.Exists(identity.CacheDir));
        Assert.False(Directory.Exists(earlier.DataDir));
        Assert.Equal(
        [
            $"shell: Moved \"{earlier.DataDir}\" to \"{identity.DataDir}\"",
            $"shell: Moved \"{earlier.StateDir}\" to \"{identity.StateDir}\""
        ], messages);

        Directory.CreateDirectory(earlier.DataDir);
        identity.CarryOver(log);

        Assert.True(Directory.Exists(earlier.DataDir));
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void A_given_shell_id_moves_nothing_and_a_given_folder_is_not_moved_into()
    {
        using var temp = new TempFolder();
        var locations = new ShellLocations(Path.Combine(temp.Path, "config"), Path.Combine(temp.Path, "local"));
        var program = Path.Combine(locations.ConfigRoot, "bar", "bin", "shell", "Bar.exe");
        var earlier = ShellIdentity.Resolve(program, locations with { ConfigRoot = "" }, null, null, null, null);
        Directory.CreateDirectory(earlier.DataDir);
        Directory.CreateDirectory(earlier.StateDir);
        var given = Path.Combine(temp.Path, "state");

        ShellIdentity.Resolve(program, locations, "bar", null, null, null).CarryOver(new Log());
        Assert.True(Directory.Exists(earlier.DataDir));

        ShellIdentity.Resolve(program, locations, null, null, given, null).CarryOver(new Log());
        Assert.True(Directory.Exists(earlier.StateDir));
        Assert.False(Directory.Exists(given));
    }

    [Fact]
    public void A_given_folder_is_next_to_the_program_and_can_name_the_base()
    {
        var identity = ShellIdentity.Resolve(@"C:\shells\mine.exe", Locations with { ConfigRoot = "" }, "id", "data",
            @"$BASE\other", @"D:\cache");

        Assert.Equal(@"C:\shells\data", identity.DataDir);
        Assert.Equal(@"C:\local\state\other", identity.StateDir);
        Assert.Equal(@"D:\cache", identity.CacheDir);
    }

    [Fact]
    public void A_shell_id_names_its_mutex_in_any_case()
    {
        Assert.Equal(DuplicateHold.ShellMutexName(Locations, "Mine"), DuplicateHold.ShellMutexName(Locations, "mine"));
    }

    private static string IdOf(string program)
    {
        return ShellIdentity.Resolve(program, Locations, null, null, null, null).ShellId;
    }
}
