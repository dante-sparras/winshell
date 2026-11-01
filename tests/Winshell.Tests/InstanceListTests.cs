using System.Diagnostics;

namespace Winshell.Tests;

public class InstanceListTests
{
    [Fact]
    public void An_instance_id_that_now_belongs_to_a_process_this_user_cannot_open_is_not_running()
    {
        // Process id 4 is always the System process, which a user cannot open.
        Assert.False(Instances.IsAlive(4));
    }

    [Fact]
    public void An_instance_id_that_now_belongs_to_another_program_is_not_running()
    {
        // This test program's own name starts with Winshell, so it takes another one. cmd waits on its open input.
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
        using var other = Process.Start(info)!;
        try
        {
            Assert.False(Instances.IsAlive(other.Id));
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
        Assert.False(Instances.IsAlive(int.MaxValue - 2));
    }
}
