using Winshell.Native;

namespace Winshell.Services.Tests;

public class ProcessImageTests
{
    [Fact]
    public void This_process_image_is_the_running_program()
    {
        var path = ProcessImage.TryRead((uint)Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_process_has_no_image()
    {
        Assert.Null(ProcessImage.TryRead(0));
    }
}
