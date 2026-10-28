using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using Winshell.Platform.Windows.Native;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Reloads a shell with real windows many times and checks that each reload gives back what the generation before it
/// held: its windows, their composition surfaces and text, their UI Automation providers, and the generation itself.
/// The counts are for the whole process, so check.ps1 runs this test alone.
/// </summary>
[Trait("Category", "Leak")]
public class ReloadLeakTests(ITestOutputHelper output)
{
    // Even, so the baseline and the end have the same windows: the panel alone.
    private const int WarmUp = 6;
    private const int Reloads = 40;

    /// <summary>Every other reload adds a floating window, so windows are made and destroyed as well as handed over.</summary>
    private static string Config(int reload, bool floating) => $$"""
        import Winshell
        import QtQuick
        import QtQuick.Controls

        ShellRoot {
          PanelWindow {
            anchors { top: true; left: true }
            exclusionMode: ExclusionMode.Ignore
            implicitWidth: 360
            implicitHeight: 48
            color: "transparent"
            WindowEffects.backdrop: WindowEffects.Acrylic

            Row {
              spacing: 4
              Text { text: "reload {{reload}}"; color: "white" }
              Button { text: "button {{reload}}" }
              Slider { width: 80; value: 0.5 }
              TextField { width: 80; text: "field" }
            }
          }
        {{(floating ? """
          FloatingWindow {
            implicitWidth: 160
            implicitHeight: 90
            Text { text: "floating" }
          }
        """ : "")}}
        }
        """;

    [Fact]
    public void Reloading_releases_what_each_generation_held()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var root = Path.Combine(dir, "shell.qml");
        File.WriteAllText(root, Config(0, floating: false));
        var messages = new List<string>();
        var log = new Log { Sink = (level, category, message) => { if (level >= LogLevel.Warn) messages.Add($"{category}: {message}"); } };
        try
        {
            using var loop = new WinEventLoop { Log = log };
            using var platform = new WindowsPlatform(loop, log);
            WindowsModules.Install(platform);
            using var host = new ShellHost(root, loop, platform, log) { WatchFiles = false };
            host.Services.Add(engine => engine.AddService<IClipboard>(platform));
            Assert.True(host.Start());

            var generations = new List<WeakReference>();
            (int Handles, uint Gdi, uint User) before = default, after = default;
            var reload = 0;
            void Step()
            {
                // Ask each window for its UI Automation element, as a screen reader would, so providers exist.
                foreach (var window in platform.Windows.ToArray())
                {
                    if (UiaNodeFromHandle(window.Handle, out var node) == 0)
                        UiaNodeRelease(node);
                }
                if (reload == WarmUp)
                    before = Resources();
                if (reload == WarmUp + Reloads)
                {
                    after = Resources();
                    loop.Quit(0);
                    return;
                }
                reload++;
                File.WriteAllText(root, Config(reload, floating: reload % 2 == 1));
                if (reload > WarmUp)
                    generations.Add(Track(host));
                host.Reload(hard: false);
                // Long enough for a frame of each window.
                loop.Schedule(TimeSpan.FromMilliseconds(40), Step);
            }
            loop.Schedule(TimeSpan.FromMilliseconds(40), Step);
            loop.Run();

            output.WriteLine($"after {Reloads} reloads: {after.Handles - before.Handles} handles, {after.Gdi - (long)before.Gdi} GDI objects, {after.User - (long)before.User} USER objects");
            Assert.Empty(messages);
            // A reload hands the panel to the new generation; the floating window comes and goes.
            Assert.Single(platform.Windows);
            Assert.Equal(0, generations.Count(g => g.IsAlive));
            // One leaked handle, GDI object or window per reload would add 40.
            Assert.InRange(after.User - (long)before.User, -100, 4);
            Assert.InRange(after.Gdi - (long)before.Gdi, -100, 8);
            Assert.InRange(after.Handles - before.Handles, -1000, 20);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    // UI Automation's own client functions, to ask for a window's element the way a client does.
    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaNodeFromHandle(nint hwnd, out nint node);

    [DllImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UiaNodeRelease(nint node);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Track(ShellHost host) => new(host.Engine);

    /// <summary>The process's kernel handles, GDI objects and USER objects, after the garbage collector has run.</summary>
    private static (int Handles, uint Gdi, uint User) Resources()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        using var process = Process.GetCurrentProcess();
        return (process.HandleCount, Win32.GetGuiResources(process.Handle, Win32.GR_GDIOBJECTS), Win32.GetGuiResources(process.Handle, Win32.GR_USEROBJECTS));
    }
}
