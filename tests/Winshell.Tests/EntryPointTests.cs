using System.Reflection;

namespace Winshell.Tests;

public class EntryPointTests
{
    // A startup error's dialog opens a folder through ShellExecute on the main thread, which wants a single-threaded
    // apartment there; in the multithreaded one an async Main gives, COM moves it to a hidden thread.
    [Fact]
    public void The_main_thread_is_a_single_threaded_apartment()
    {
        var main = typeof(Cli).Assembly.EntryPoint;
        Assert.NotNull(main);
        Assert.NotNull(main.GetCustomAttribute<STAThreadAttribute>());
    }
}
