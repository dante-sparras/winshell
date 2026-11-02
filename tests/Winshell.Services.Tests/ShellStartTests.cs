namespace Winshell.Services.Tests;

/// <summary>What a start reads before it is a shell: its command line, its id, and its folders.</summary>
public sealed class ShellStartTests
{
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
    }

    [Fact]
    public void A_given_shell_id_names_the_folders()
    {
        var identity = ShellIdentity.Resolve(@"C:\shells\mine\mine.exe", @"C:\config", "mine-id", null, null, null);

        Assert.Equal("mine-id", identity.ShellId);
        Assert.Equal(Path.Combine(ShellLocations.DataRoot, "by-shell", "mine-id"), identity.DataDir);
        Assert.Equal(Path.Combine(ShellLocations.StateRoot, "by-shell", "mine-id"), identity.StateDir);
        Assert.Equal(Path.Combine(ShellLocations.CacheRoot, "by-shell", "mine-id"), identity.CacheDir);
    }

    [Theory]
    [InlineData(@"C:\config\mine.exe", "root")]
    [InlineData(@"C:\config\Bar\bar.exe", "bar")]
    public void A_program_in_the_config_folder_is_named_after_its_folder(string program, string id)
    {
        Assert.Equal(id, ShellIdentity.Resolve(program, @"C:\config", null, null, null, null).ShellId);
    }

    [Fact]
    public void A_program_anywhere_else_is_named_after_its_path_in_any_case()
    {
        var one = ShellIdentity.Resolve(@"C:\shells\mine.exe", @"C:\config", null, null, null, null).ShellId;
        var same = ShellIdentity.Resolve(@"C:\SHELLS\MINE.EXE", @"C:\config", null, null, null, null).ShellId;
        var other = ShellIdentity.Resolve(@"C:\shells\yours.exe", @"C:\config", null, null, null, null).ShellId;

        Assert.Equal(one, same);
        Assert.NotEqual(one, other);
        Assert.Matches("^[0-9a-f]{32}$", one);
    }

    [Fact]
    public void A_given_folder_is_next_to_the_program_and_can_name_the_base()
    {
        var identity = ShellIdentity.Resolve(@"C:\shells\mine.exe", "", "id", "data", @"$BASE\other", @"D:\cache");

        Assert.Equal(@"C:\shells\data", identity.DataDir);
        Assert.Equal(Path.Combine(ShellLocations.StateRoot, "other"), identity.StateDir);
        Assert.Equal(@"D:\cache", identity.CacheDir);
    }

    [Fact]
    public void A_config_and_a_shell_id_never_share_a_mutex()
    {
        Assert.Equal(DuplicateHold.ShellMutexName("Mine"), DuplicateHold.ShellMutexName("mine"));
        Assert.NotEqual(DuplicateHold.ConfigMutexName(@"C:\mine"), DuplicateHold.ShellMutexName(@"C:\mine"));
    }
}
