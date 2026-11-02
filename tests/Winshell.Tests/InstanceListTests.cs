using System.Diagnostics;

namespace Winshell.Tests;

public class InstanceListTests
{
    private static InstanceInfo Record(int pid, string? processName) =>
        new("id", pid, "shell", "C:\\cfg\\shell.qml", DateTime.Now, "pipe", "log.txt", ProcessName: processName);

    [Theory]
    [InlineData(null)]
    [InlineData("myshell")]
    public void An_instance_id_that_now_belongs_to_a_process_this_user_cannot_open_is_not_running(string? processName)
    {
        // Process id 4 is always the System process, which a user cannot open.
        Assert.False(Instances.IsCurrent(Record(4, processName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("myshell")]
    public void An_instance_id_that_now_belongs_to_another_program_is_not_running(string? processName)
    {
        // This test program's own name starts with Winshell, so it takes another one. cmd waits on its open input.
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        using var other = Process.Start(info)!;
        try
        {
            Assert.False(Instances.IsCurrent(Record(other.Id, processName)));
        }
        finally
        {
            other.Kill();
        }
    }

    [Fact]
    public void A_configuration_program_of_any_name_is_running_under_the_name_it_recorded()
    {
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        using var other = Process.Start(info)!;
        try
        {
            Assert.True(Instances.IsCurrent(Record(other.Id, "cmd")));
        }
        finally
        {
            other.Kill();
        }
    }

    [Fact]
    public void An_instance_id_no_process_has_is_not_running()
    {
        // Process ids are multiples of four, so this one is never in use.
        Assert.False(Instances.IsCurrent(Record(int.MaxValue - 2, "myshell")));
    }
}
