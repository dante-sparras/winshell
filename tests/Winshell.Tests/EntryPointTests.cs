using System.Reflection;

namespace Winshell.Tests;

public class EntryPointTests
{
    // The shell's event loop runs on the main thread. Shell objects and ShellExecute want a single-threaded
    // apartment there; in the multithreaded one an async Main gives, COM moves them to a hidden thread.
    [Fact]
    public void The_main_thread_is_a_single_threaded_apartment()
    {
        var main = typeof(Cli).Assembly.EntryPoint;
        Assert.NotNull(main);
        Assert.NotNull(main.GetCustomAttribute<STAThreadAttribute>());
    }
}
