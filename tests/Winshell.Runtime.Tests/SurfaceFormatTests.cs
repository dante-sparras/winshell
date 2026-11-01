using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>
/// <c>surfaceFormat</c> and <c>resourcesLost</c>. The platform records the opaque value it was asked for, which stays
/// null until QML sets it, so an unset window keeps the premultiplied chain.
/// </summary>
public class SurfaceFormatTests
{
    [Fact]
    public void An_unset_surface_format_follows_the_color_and_leaves_the_chain_premultiplied()
    {
        using var white = new Shell("""
            import Winshell
            PanelWindow {
              property bool opaque: surfaceFormat.opaque
            }
            """);
        white.Start();
        var whiteWindow = white.Platform.Windows.Single();
        var whitePanel = (PanelWindow)whiteWindow.Client;
        Assert.True(whitePanel.SurfaceFormat.Opaque);
        Assert.Null(whiteWindow.SurfaceOpaque);
        Assert.Equal(true, white.Files.Eval(whitePanel, "opaque"));

        whitePanel.Color = QColor.Transparent;
        Assert.False(whitePanel.SurfaceFormat.Opaque);
        Assert.Null(whitePanel.SurfaceOpaque);
        Assert.Null(whiteWindow.SurfaceOpaque);

        using var clear = new Shell("""
            import Winshell
            PanelWindow {
              color: "transparent"
              property bool opaque: surfaceFormat.opaque
            }
            """);
        clear.Start();
        var clearWindow = clear.Platform.Windows.Single();
        Assert.False(((PanelWindow)clearWindow.Client).SurfaceFormat.Opaque);
        Assert.Null(clearWindow.SurfaceOpaque);
    }

    [Fact]
    public void An_explicit_surface_format_is_what_the_platform_sees()
    {
        using var opaque = new Shell("""
            import Winshell
            PanelWindow {
              surfaceFormat.opaque: true
              property bool opaque: surfaceFormat.opaque
            }
            """);
        opaque.Start();
        var opaqueWindow = opaque.Platform.Windows.Single();
        Assert.True(((PanelWindow)opaqueWindow.Client).SurfaceFormat.Opaque);
        Assert.Equal(true, opaqueWindow.SurfaceOpaque);

        using var clear = new Shell("""
            import Winshell
            PanelWindow {
              color: "transparent"
              surfaceFormat { opaque: false }
              property bool opaque: surfaceFormat.opaque
            }
            """);
        clear.Start();
        var clearWindow = clear.Platform.Windows.Single();
        var clearPanel = (PanelWindow)clearWindow.Client;
        Assert.False(clearPanel.SurfaceFormat.Opaque);
        Assert.Equal(false, clearWindow.SurfaceOpaque);

        clearPanel.Color = QColor.White;
        Assert.False(clearPanel.SurfaceFormat.Opaque);
        Assert.Equal(false, clearPanel.SurfaceOpaque);
    }

    [Fact]
    public void A_surface_format_change_after_the_window_exists_is_ignored()
    {
        using var shell = new Shell("""
            import Winshell
            PanelWindow {
              property bool opaque: surfaceFormat.opaque
              onWindowConnected: surfaceFormat.opaque = false
            }
            """);
        shell.Start();
        var window = shell.Platform.Windows.Single();
        var panel = (PanelWindow)window.Client;
        Assert.True(panel.SurfaceFormat.Opaque);
        Assert.Null(window.SurfaceOpaque);
        Assert.Contains(shell.Files.Messages, m => m == "Warn window: Cannot set window surface format.");

        using var same = new Shell("""
            import Winshell
            PanelWindow {
              color: "transparent"
              onWindowConnected: surfaceFormat.opaque = false
            }
            """);
        same.Start();
        var sameWindow = same.Platform.Windows.Single();
        Assert.False(((PanelWindow)sameWindow.Client).SurfaceFormat.Opaque);
        Assert.Null(sameWindow.SurfaceOpaque);
        Assert.Contains(same.Files.Messages, m => m == "Warn window: Cannot set window surface format.");
    }

    [Fact]
    public void A_reload_hands_the_new_surface_format_to_the_same_native_window()
    {
        using var shell = new Shell("""
            import Winshell
            PanelWindow { }
            """);
        shell.Start();
        var window = shell.Platform.Windows.Single();
        Assert.Null(window.SurfaceOpaque);

        shell.Files.Write("shell.qml", """
            import Winshell
            PanelWindow { surfaceFormat.opaque: true }
            """);
        Assert.True(shell.Host.TryReload(false), string.Join('\n', shell.Files.Messages));
        shell.Files.Loop.Process();
        Assert.Same(window, shell.Platform.Windows.Single());
        Assert.Equal(true, window.SurfaceOpaque);
    }

    [Fact]
    public void Resources_lost_is_followed_by_closed_and_the_window_is_not_opened_again()
    {
        using var shell = new Shell("""
            import Winshell
            PanelWindow {
              property string order: ""
              onResourcesLost: order += "lost,"
              onClosed: order += "closed,"
            }
            """);
        shell.Platform.FailResourceLoss = true;
        shell.Start();
        var panel = (PanelWindow)shell.Host.Root!;
        Assert.Equal("lost,closed,", panel.ReadProperty("order"));
        Assert.False(panel.Visible);
        Assert.Equal(1, shell.Platform.Creations);
        Assert.Contains(shell.Files.Messages, m => m.Contains("lost the resources it draws with", StringComparison.Ordinal));
        Assert.DoesNotContain(shell.Files.Messages, m => m.Contains("opens again", StringComparison.Ordinal));

        panel.OnResourcesLost(new SurfaceResourcesException("again"));
        Assert.Equal("lost,closed,", panel.ReadProperty("order"));
        shell.Files.Loop.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, shell.Platform.Creations);
        Assert.Empty(shell.Platform.Windows);
    }

    private sealed class Shell : IDisposable
    {
        public Shell(string qml)
        {
            Files = new TestShell(("shell.qml", qml));
            Host = new ShellHost(Path.Combine(Files.Directory, "shell.qml"), Files.Loop, Platform, Files.Log);
        }

        public TestShell Files { get; }

        public FakePlatform Platform { get; } = new();

        public ShellHost Host { get; }

        public void Start()
        {
            Assert.True(Host.Start(), string.Join('\n', Files.Messages));
            Files.Loop.Process();
        }

        public void Dispose()
        {
            Host.Dispose();
            Files.Dispose();
        }
    }
}
