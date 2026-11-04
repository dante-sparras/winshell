using Winshell.Native;

namespace Winshell.Services.Tests;

public sealed class ProcessIntegrityTests
{
    [Fact]
    public void The_running_process_reads_between_medium_and_system_and_the_idle_process_reads_as_unknown()
    {
        // Medium on a developer's desktop, high on CI.
        Assert.InRange(ProcessIntegrity.TryRead((uint)Environment.ProcessId) ?? 0, ProcessIntegrity.Medium, 0x4000u);
        // The System Idle Process has no token to read.
        Assert.Null(ProcessIntegrity.TryRead(0));
    }
}
