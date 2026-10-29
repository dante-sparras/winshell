using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>Async FileView saves stay in order and do not share one temp file.</summary>
public class FileViewWriteQueueTests
{
    [Fact]
    public void Async_writes_land_in_order_and_leave_no_temp_file()
    {
        using var files = new TestShell(("shell.qml", """
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              property alias view: view
              FileView {
                id: view
                preload: false
              }
            }
            """));
        var file = Path.Combine(files.Directory, "notes.txt");
        var host = new ShellHost(Path.Combine(files.Directory, "shell.qml"), files.Loop, null, files.Log);
        try
        {
            Assert.True(host.Start(), string.Join("\n", files.Messages));
            files.Loop.Process();
            var view = (QObject)host.Root!.ReadProperty("view")!;
            view.WriteProperty("path", file);
            Assert.Equal(file, view.ReadProperty("path"));
            var setText = view.Meta.Methods["setText"];
            for (var i = 0; i < 40; i++)
                setText.Invoke(view, ["value-" + i]);

            var until = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                files.Loop.Process();
                string? text = null;
                var temps = Array.Empty<string>();
                try
                {
                    if (File.Exists(file))
                        text = File.ReadAllText(file);
                    temps = Directory.GetFiles(files.Directory, "*.tmp-*");
                }
                catch (IOException)
                {
                }
                if (text == "value-39" && temps.Length == 0)
                {
                    Thread.Sleep(150);
                    files.Loop.Process();
                    Assert.Equal("value-39", File.ReadAllText(file));
                    Assert.Empty(Directory.GetFiles(files.Directory, "*.tmp-*"));
                    return;
                }
                Assert.True(DateTime.UtcNow < until,
                    $"exists={File.Exists(file)} files=[{string.Join(", ", Directory.GetFiles(files.Directory))}] logs=[{string.Join(" | ", files.Messages)}]");
                Thread.Sleep(20);
            }
        }
        finally
        {
            host.Dispose();
        }
    }
}
