using SkiaSharp;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

/// <summary>A platform with screens that tests set, and windows that record what they were told.</summary>
public sealed class FakePlatform : IShellPlatform
{
    public List<ScreenInfo> ScreenList { get; } =
    [
        new("1", "DISPLAY1", "Monitor A", "Acme", "S1", new QRect(0, 0, 1920, 1080), new QRect(0, 0, 1920, 1040), 1, 600, 340, 60, true),
        new("2", "DISPLAY2", "Monitor B", "Acme", "S2", new QRect(1920, 0, 2560, 1440), new QRect(1920, 0, 2560, 1440), 1.5, 600, 340, 144, false),
    ];

    public IReadOnlyList<ScreenInfo> Screens => ScreenList;

    public event Action? ScreensChanged;

    public List<FakeWindow> Windows { get; } = [];

    public void ChangeScreens() => ScreensChanged?.Invoke();

    /// <summary>How many of the next native windows fail to be made, as a failing CreateWindowEx would.</summary>
    public int FailCreations { get; set; }

    /// <summary>How many of the next panel placements throw.</summary>
    public int FailPlacements { get; set; }

    public int Creations { get; private set; }

    public IWindowBackend CreateWindow(WindowKind kind, IWindowClient client)
    {
        Creations++;
        if (FailCreations > 0)
        {
            FailCreations--;
            throw new InvalidOperationException("the native window could not be made");
        }
        var w = new FakeWindow(kind, client, this);
        Windows.Add(w);
        return w;
    }
}

public sealed class FakeWindow(WindowKind kind, IWindowClient client, FakePlatform platform) : IWindowBackend, IRebindable
{
    public WindowKind Kind { get; } = kind;

    public IWindowClient Client { get; private set; } = client;

    public PanelPlacement? Panel { get; private set; }

    public PopupPlacement? Popup { get; private set; }

    public bool Disposed { get; private set; }

    public bool IsVisible { get; private set; }

    public QPoint Origin { get; private set; }

    public (int X, int Y) MapToScreen(double x, double y) => ((int)Math.Round(Origin.X + x), (int)Math.Round(Origin.Y + y));

    public int Frames { get; set; }

    public void Rebind(IWindowClient client) => Client = client;

    public void SetPanel(PanelPlacement placement)
    {
        if (platform.FailPlacements > 0)
        {
            platform.FailPlacements--;
            throw new InvalidOperationException("the panel could not be placed");
        }
        Panel = placement;
        var screen = placement.Screen?.Geometry ?? new QRect(0, 0, 1920, 1080);
        var w = placement.Anchors.HasFlag(Edges.Left) && placement.Anchors.HasFlag(Edges.Right) ? screen.Width - placement.MarginLeft - placement.MarginRight : placement.Width;
        var h = placement.Anchors.HasFlag(Edges.Top) && placement.Anchors.HasFlag(Edges.Bottom) ? screen.Height - placement.MarginTop - placement.MarginBottom : placement.Height;
        Origin = new QPoint(screen.X + placement.MarginLeft, screen.Y + placement.MarginTop);
        Client.OnResized(w, h, placement.Screen?.DevicePixelRatio ?? 1);
    }

    public void SetPopup(PopupPlacement placement)
    {
        Popup = placement;
        Origin = new QPoint(placement.Rect.X, placement.Rect.Y);
        Client.OnResized(placement.Rect.Width, placement.Rect.Height, 1);
    }

    public void SetFloating(FloatingPlacement placement) => Client.OnResized(placement.Width, placement.Height, 1);

    public void SetVisible(bool visible) => IsVisible = visible;

    public void SetInputRegion(IReadOnlyList<QRect>? rects) => InputRegion = rects;

    public IReadOnlyList<QRect>? InputRegion { get; private set; }

    public void SetCursor(int shape)
    {
    }

    public void SetBackdrop(WindowBackdrop backdrop, WindowCorners corners, bool darkFrame) => Backdrop = backdrop;

    public WindowBackdrop Backdrop { get; private set; }

    public void RequestFrame() => Frames++;

    public void Raise()
    {
    }

    public void StartSystemMove()
    {
    }

    public void StartSystemResize(Edges edges)
    {
    }

    /// <summary>How many times the window asked for the keyboard.</summary>
    public int KeyboardRequests { get; private set; }

    public void SetKeyboardFocus(bool focus)
    {
        if (focus)
            KeyboardRequests++;
    }

    public SKBitmap Render()
    {
        var w = (int)Math.Ceiling(Client.Scene.Root.Width);
        var h = (int)Math.Ceiling(Client.Scene.Root.Height);
        var bitmap = new SKBitmap(Math.Max(1, w), Math.Max(1, h));
        using var canvas = new SKCanvas(bitmap);
        Client.OnPaint(canvas);
        return bitmap;
    }

    public void Dispose()
    {
        Disposed = true;
        platform.Windows.Remove(this);
    }
}

public class ShellTests
{
    private sealed class Shell : IDisposable
    {
        public Shell(params (string Name, string Text)[] files)
            : this(null, files)
        {
        }

        public Shell(Action<QmlEngine>? configure, params (string Name, string Text)[] files)
        {
            Files = new TestShell(files);
            Host = new ShellHost(Path.Combine(Files.Directory, "shell.qml"), Files.Loop, Platform, Files.Log, configure: configure);
        }

        public TestShell Files { get; }

        public FakePlatform Platform { get; } = new();

        public ShellHost Host { get; }

        public void Start()
        {
            Assert.True(Host.Start(), string.Join("\n", Files.Messages));
            Files.Loop.Process();
        }

        public void Dispose()
        {
            Host.Dispose();
            Files.Dispose();
        }
    }

    [Fact]
    public void A_panel_takes_the_keyboard_when_it_becomes_focusable_while_an_item_in_it_has_focus()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            PanelWindow {
              property Item first: one
              property Item focused: activeFocusItem
              anchors.bottom: true
              implicitWidth: 200
              implicitHeight: 40
              MouseArea { id: one; objectName: "one"; width: 40; height: 40; activeFocusOnTab: true }
              MouseArea { objectName: "two"; x: 40; width: 40; height: 40; activeFocusOnTab: true }
            }
            """));
        shell.Start();
        var window = shell.Platform.Windows.Single();
        var panel = (PanelWindow)window.Client;
        var one = (Winshell.Runtime.Quick.Item)panel.ReadProperty("first")!;

        // Not focusable: focus moves inside the panel, but the panel leaves the keyboard where it is.
        one.ForceActiveFocus();
        shell.Files.Loop.Process();
        Assert.Same(one, panel.ReadProperty("focused"));
        Assert.Equal(0, window.KeyboardRequests);

        panel.Focusable = true;
        Assert.Equal(1, window.KeyboardRequests);

        // Focusable, it asks again when focus moves.
        one.NextItemInFocusChain()!.ForceActiveFocus(QtEnums.TabFocusReason);
        Assert.Equal("two", ((Winshell.Runtime.Quick.Item)panel.ReadProperty("focused")!).ObjectName);
        Assert.Equal(2, window.KeyboardRequests);

        // Without a focused item, becoming focusable asks for nothing.
        panel.Focusable = false;
        panel.Scene.SetFocusItem(null);
        panel.Focusable = true;
        Assert.Equal(2, window.KeyboardRequests);
    }

    [Fact]
    public void The_guide_bar_makes_a_panel_per_screen()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick

            Scope {
              id: root
              property string time: "12:00"

              Variants {
                model: Shell.screens

                PanelWindow {
                  required property var modelData
                  screen: modelData

                  anchors {
                    top: true
                    left: true
                    right: true
                  }

                  implicitHeight: 30

                  Text {
                    anchors.centerIn: parent
                    text: root.time
                  }
                }
              }
            }
            """));
        shell.Start();
        Assert.Equal(2, shell.Platform.Windows.Count);
        var first = shell.Platform.Windows[0];
        Assert.True(first.IsVisible);
        Assert.Equal(Edges.Top | Edges.Left | Edges.Right, first.Panel!.Anchors);
        Assert.Equal(30, first.Panel.ExclusiveZone);
        Assert.Equal("DISPLAY1", first.Panel.Screen!.Name);
        Assert.Equal(1920, first.Client.Scene.Root.Width);
        Assert.Equal("DISPLAY2", shell.Platform.Windows[1].Panel!.Screen!.Name);
        Assert.Empty(shell.Files.Messages);

        // Unplugging a screen removes its bar.
        shell.Platform.ScreenList.RemoveAt(1);
        shell.Platform.ChangeScreens();
        shell.Files.Loop.Process();
        Assert.Single(shell.Platform.Windows);
    }

    [Fact]
    public void A_reload_reuses_the_windows_of_the_old_shell()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            ShellRoot {
              Variants {
                model: Shell.screens
                PanelWindow { required property var modelData; screen: modelData; anchors.top: true; implicitHeight: 20 }
              }
            }
            """));
        shell.Start();
        var before = shell.Platform.Windows.ToList();
        shell.Host.Reload(hard: false);
        shell.Files.Loop.Process();
        Assert.Equal(before, shell.Platform.Windows);
        Assert.All(before, w => Assert.False(w.Disposed));
    }

    [Fact]
    public void Singletons_and_SystemClock()
    {
        using var shell = new Shell(
            ("shell.qml", """
                import Winshell
                import QtQuick
                Scope {
                  property string shown: Time.time
                }
                """),
            ("Time.qml", """
                pragma Singleton
                import Winshell
                import QtQuick
                Singleton {
                  id: root
                  readonly property string time: Qt.formatDateTime(clock.date, "yyyy")
                  SystemClock { id: clock; precision: SystemClock.Seconds }
                }
                """));
        shell.Start();
        Assert.Equal(DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), shell.Host.Root!.ReadProperty("shown"));
    }

    [Fact]
    public void SystemClock_reads_the_engines_time_provider()
    {
        var now = new DateTimeOffset(new DateTime(2026, 10, 29, 9, 41, 7, DateTimeKind.Local));
        using var shell = new Shell(e => e.AddService<TimeProvider>(new FixedTime(now)), ("shell.qml", """
            import Winshell
            import QtQuick
            Scope {
              property string shown: Qt.formatDateTime(clock.date, "yyyy-MM-dd HH:mm:ss")
              SystemClock { id: clock; precision: SystemClock.Minutes }
            }
            """));
        shell.Start();
        Assert.Equal("2026-10-29 09:41:00", shell.Host.Root!.ReadProperty("shown"));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Process_collects_stdout()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              id: root
              property string out: ""
              property int code: -1
              Process {
                command: ["cmd", "/c", "echo hello"]
                running: true
                stdout: StdioCollector { onStreamFinished: root.out = this.text.trim() }
                onExited: (exitCode, exitStatus) => root.code = exitCode
              }
            }
            """));
        shell.Start();
        for (var i = 0; i < 100 && (int)shell.Host.Root!.ReadProperty("code")! != 0; i++)
        {
            await Task.Delay(50);
            shell.Files.Loop.Process();
        }
        Assert.Equal("hello", shell.Host.Root!.ReadProperty("out"));
        Assert.Equal(0, shell.Host.Root.ReadProperty("code"));
    }

    [Fact]
    public void FileView_and_JsonAdapter_read_and_write_json()
    {
        using var shell = new Shell(
            ("shell.qml", """
                import Winshell
                import Winshell.Io
                import QtQuick
                Scope {
                  property alias name: adapter.name
                  FileView {
                    id: view
                    path: Shell.shellPath("settings.json")
                    blockLoading: true
                    blockWrites: true
                    onAdapterUpdated: writeAdapter()
                    JsonAdapter {
                      id: adapter
                      property string name: "default"
                      property int size: 3
                      property JsonObject nested: JsonObject { property bool on: false }
                    }
                  }
                }
                """),
            ("settings.json", """{ "name": "from file", "nested": { "on": true } }"""));
        shell.Start();
        var root = shell.Host.Root!;
        Assert.Equal("from file", root.ReadProperty("name"));
        root.WriteProperty("name", "changed");
        shell.Files.Loop.Process();
        var written = File.ReadAllText(Path.Combine(shell.Files.Directory, "settings.json"));
        Assert.Contains("\"name\": \"changed\"", written, StringComparison.Ordinal);
        Assert.Contains("\"on\": true", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_FileView_writes_once_per_change_and_follows_a_folder_its_first_write_made()
    {
        using var shell = new Shell(
            ("shell.qml", """
                import Winshell
                import Winshell.Io
                import QtQuick
                Scope {
                  property alias name: adapter.name
                  property int writes: 0
                  property int changes: 0
                  FileView {
                    path: Shell.shellPath("later/settings.json")
                    blockLoading: true
                    blockWrites: true
                    watchChanges: true
                    onAdapterUpdated: { writes++; writeAdapter() }
                    onFileChanged: { changes++; reload() }
                    JsonAdapter {
                      id: adapter
                      property string name: "default"
                    }
                  }
                }
                """));
        shell.Start();
        var root = shell.Host.Root!;
        var file = Path.Combine(shell.Files.Directory, "later", "settings.json");
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));

        // The first write makes the folder.
        root.WriteProperty("name", "chosen");
        shell.Files.Loop.Process();
        Assert.Equal(1, root.ReadProperty("writes"));
        Assert.True(File.Exists(file));

        // A change made by hand afterwards is followed.
        Thread.Sleep(300);
        File.WriteAllText(file, """{ "name": "by hand" }""");
        var until = DateTime.UtcNow.AddSeconds(5);
        while ((string?)root.ReadProperty("name") != "by hand")
        {
            Assert.True(DateTime.UtcNow < until, "The FileView did not follow the file.");
            Thread.Sleep(20);
            shell.Files.Loop.Process();
        }
        Assert.Equal(1, root.ReadProperty("writes"));
    }

    [Fact]
    public void IpcHandler_functions_can_be_called()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import Winshell.Io
            import QtQuick
            Scope {
              id: root
              property real angle: 0
              IpcHandler {
                target: "rect"
                function setAngle(angle: real): void { root.angle = angle }
                function getAngle(): real { return root.angle }
                function hello(name: string): string { return "hi " + name }
              }
            }
            """));
        shell.Start();
        var ipc = shell.Host.Ipc;
        Assert.Equal("", ipc.Call("rect", "setAngle", ["40.5"]));
        Assert.Equal("40.5", ipc.Call("rect", "getAngle", []));
        Assert.Equal("hi there", ipc.Call("rect", "hello", ["there"]));
        Assert.Contains("function setAngle(angle: real): void", ipc.Show(), StringComparison.Ordinal);
        Assert.Throws<IpcException>(() => ipc.Call("nope", "x", []));
    }

    [Fact]
    public void Popups_open_next_to_their_anchor_item()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            PanelWindow {
              id: bar
              anchors { top: true; left: true; right: true }
              implicitHeight: 30
              Rectangle { id: button; x: 100; width: 40; height: 30 }
              PopupWindow {
                objectName: "popup"
                anchor.item: button
                anchor.edges: Edges.Bottom | Edges.Left
                implicitWidth: 200
                implicitHeight: 100
                visible: true
              }
            }
            """));
        shell.Start();
        var popup = shell.Platform.Windows.Single(w => w.Kind == WindowKind.Popup);
        Assert.True(popup.IsVisible);
        Assert.Equal(new QRect(100, 30, 200, 100), popup.Popup!.Rect);
    }

    [Fact]
    public void A_screen_reports_its_work_area_and_follows_changes()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            ShellRoot {
              property var screen: Shell.screens[1]
              property real workY: screen.workArea.y
              property real workHeight: screen.workArea.height
              property real ratio: screen.devicePixelRatio
              property int rate: screen.refreshRate
            }
            """));
        shell.Start();
        var root = shell.Host.Root!;
        Assert.Equal(1440.0, Convert.ToDouble(root.ReadProperty("workHeight")));

        // The taskbar moves to the top of the second screen, its scale goes to 200%, and it runs at 60 Hz.
        shell.Platform.ScreenList[1] = shell.Platform.ScreenList[1] with
        {
            Geometry = new QRect(1920, 0, 1920, 1080),
            WorkArea = new QRect(1920, 24, 1920, 1056),
            DevicePixelRatio = 2,
            RefreshRate = 60,
        };
        shell.Platform.ChangeScreens();
        shell.Files.Loop.Process();
        Assert.Equal(24.0, Convert.ToDouble(root.ReadProperty("workY")));
        Assert.Equal(1056.0, Convert.ToDouble(root.ReadProperty("workHeight")));
        Assert.Equal(2.0, Convert.ToDouble(root.ReadProperty("ratio")));
        Assert.Equal(60, Convert.ToInt32(root.ReadProperty("rate")));
    }

    [Fact]
    public void A_window_reports_the_screen_it_was_moved_to()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            ShellRoot {
              property string where: window.screen.name
              FloatingWindow { id: window; visible: true }
            }
            """));
        shell.Start();
        var root = shell.Host.Root!;
        Assert.Equal("DISPLAY1", root.ReadProperty("where"));

        shell.Platform.Windows.Single().Client.OnScreenChanged("2");
        shell.Files.Loop.Process();
        Assert.Equal("DISPLAY2", root.ReadProperty("where"));
    }

    [Fact]
    public void A_popup_is_placed_on_the_screen_its_parent_was_moved_to()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            FloatingWindow {
              id: window
              visible: true
              Rectangle { id: button; width: 40; height: 30 }
              PopupWindow {
                anchor.item: button
                anchor.edges: Edges.Bottom | Edges.Left
                implicitWidth: 200
                implicitHeight: 100
                visible: window.screen.name === "DISPLAY2"
              }
            }
            """));
        shell.Start();
        shell.Platform.Windows.Single(w => w.Kind == WindowKind.Floating).Client.OnScreenChanged("2");
        shell.Files.Loop.Process();
        var popup = shell.Platform.Windows.Single(w => w.Kind == WindowKind.Popup);
        Assert.True(popup.IsVisible);
        Assert.Equal("2", popup.Popup!.Screen!.Id);
    }

    [Fact]
    public void Window_masks_become_input_regions()
    {
        using var shell = new Shell(("shell.qml", """
            import Winshell
            import QtQuick
            PanelWindow {
              anchors { top: true; left: true }
              implicitWidth: 200; implicitHeight: 100
              color: "transparent"
              mask: Region { item: rect }
              Rectangle { id: rect; x: 10; y: 20; width: 30; height: 40 }
            }
            """));
        shell.Start();
        var window = shell.Platform.Windows.Single();
        Assert.Equal([new QRect(10, 20, 30, 40)], window.InputRegion!);
    }
}
