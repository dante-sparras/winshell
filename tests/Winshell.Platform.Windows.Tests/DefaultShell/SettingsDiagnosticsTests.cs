using System.Diagnostics;
using SkiaSharp;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Runtime;
using Winshell.Runtime.Quick;
using Winshell.Runtime.Quick.Controls;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// Run diagnostics on Winshell Settings' About page, against a doctor that returns fixed checks, so nothing of this
/// PC is read.
/// </summary>
[Collection(ThemeCollection.Name)]
public class SettingsDiagnosticsTests
{
    private const string View = """
        import Winshell
        import QtQuick
        SettingsView {
          anchors.fill: parent
          showIcons: false
          page: "about"
        }
        """;

    private sealed class FixedDoctor(params DoctorCheck[] checks) : IDoctor
    {
        public int Runs { get; private set; }

        public IReadOnlyList<DoctorCheck> Run()
        {
            Runs++;
            return checks;
        }
    }

    private static readonly DoctorCheck[] Checks =
    [
        new("windows", "Windows", CheckStatus.Pass, "Windows 11 26H2, build 26300.1"),
        new("signIn", "Sign-in", CheckStatus.Warn, "Startup apps turned it off.", "Turn Winshell on in Settings > Apps > Startup."),
        new("config", "Config", CheckStatus.Fail, "shell.qml[1:1]: Expected }", "Correct that line."),
    ];

    private static Item Card(DefaultShellHarness shell, string key) => shell.Find("setting:" + key);

    /// <summary>Settles until the run, which happens off the loop, has come back.</summary>
    private static void WaitForRun(DefaultShellHarness shell, QObject diagnostics)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            Thread.Sleep(10);
            shell.Settle();
        }
        while ((bool)diagnostics.ReadProperty("running")! && watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.False((bool)diagnostics.ReadProperty("running")!);
    }

    private static QObject Diagnostics(DefaultShellHarness shell) =>
        shell.Engine.FindNativeSingleton<DiagnosticsService>() ?? throw new InvalidOperationException("The page made no Diagnostics.");

    [Fact]
    public void Run_diagnostics_lists_each_check_with_its_fix_and_counts_them()
    {
        var doctor = new FixedDoctor(Checks);
        using var shell = new DefaultShellHarness(View, 1000, 900, configure: e => e.AddService<IDoctor>(doctor));
        shell.Settle();
        Assert.False(shell.Has("diagnosticsResults"));
        Assert.Equal("Check how this PC is set up for Winshell, as winshell doctor does", Card(shell, "diagnostics").ReadProperty("description"));

        var run = shell.Find("runDiagnostics");
        Assert.True(run.Enabled);
        shell.Click(run);
        WaitForRun(shell, Diagnostics(shell));

        Assert.Equal(1, doctor.Runs);
        Assert.Equal("1 passed, 1 warning, 1 failed.", Card(shell, "diagnostics").ReadProperty("description"));
        Assert.True(shell.Has("diagnosticsResults"));
        string[] Rows() => DefaultShellHarness.FindAll(shell.Find("diagnosticsResults"))
            .Where(i => i.ObjectName.StartsWith("check:", StringComparison.Ordinal) && i.Visible)
            .OrderBy(i => i.MapToScene(0, 0).Y).Select(i => i.ObjectName).ToArray();
        Assert.Equal(["check:windows", "check:signIn", "check:config"], Rows());
        Assert.False(shell.Has("fix:windows"));
        Assert.Equal("Fix: Correct that line.", shell.Find("fix:config").ReadProperty("text"));

        // Narrator reads a row as one line: what it found, and how to fix it.
        var tree = new AccessibleTree(shell.Scene);
        tree.Refresh();
        var names = Flatten(tree.Root).Select(n => n.Name).ToList();
        Assert.Contains("Warning: Sign-in. Startup apps turned it off. Fix: Turn Winshell on in Settings > Apps > Startup.", names);
        Assert.Contains("Passed: Windows. Windows 11 26H2, build 26300.1", names);
        using (var render = Paint(shell))
            Save(render, "settings-about-diagnostics");

        // A second run replaces the rows.
        shell.Click(shell.Find("runDiagnostics"));
        WaitForRun(shell, Diagnostics(shell));
        Assert.Equal(2, doctor.Runs);
        Assert.Equal(3, Rows().Length);
        Assert.Empty(shell.Messages);
    }

    [Fact]
    public void Without_winshell_exe_there_is_nothing_to_run()
    {
        using var shell = new DefaultShellHarness(View, 1000, 900);
        shell.Settle();

        Assert.False(shell.Find("runDiagnostics").Enabled);
        Assert.Equal("Only a shell started by winshell.exe can check its setup", Card(shell, "diagnostics").ReadProperty("description"));
    }

    [Fact]
    public void A_doctor_that_throws_shows_why()
    {
        using var shell = new DefaultShellHarness(View, 1000, 900, configure: e => e.AddService<IDoctor>(new ThrowingDoctor()));
        shell.Settle();
        shell.Click(shell.Find("runDiagnostics"));
        WaitForRun(shell, Diagnostics(shell));

        Assert.Equal("0 passed, 0 warnings, 1 failed.", Card(shell, "diagnostics").ReadProperty("description"));
        Assert.Contains(DefaultShellHarness.FindAll(shell.Find("check:doctor")).OfType<Text>(), t => t.Text_ == "Could not run: InvalidOperationException: the registry is gone");
    }

    private sealed class ThrowingDoctor : IDoctor
    {
        public IReadOnlyList<DoctorCheck> Run() => throw new InvalidOperationException("the registry is gone");
    }

    private static IEnumerable<AccessibleNode> Flatten(AccessibleNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    /// <summary>Paints the scene on the Mica base color, in physical pixels at the scene's scale.</summary>
    private static SKBitmap Paint(DefaultShellHarness shell)
    {
        shell.Loop.Advance(TimeSpan.FromSeconds(1));
        shell.Settle();
        var scale = shell.Scene.DevicePixelRatio;
        var bitmap = new SKBitmap((int)(shell.Scene.Root.Width * scale), (int)(shell.Scene.Root.Height * scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Theme.Current.SolidBackgroundFillColorBase.ToSk());
        shell.Scene.Paint(canvas, shell.Loop.Now);
        return bitmap;
    }

    /// <summary>Writes a render to the folder WINSHELL_SETTINGS_EVIDENCE names, if any. Nothing is committed.</summary>
    private static void Save(SKBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("WINSHELL_SETTINGS_EVIDENCE") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), png.ToArray());
    }
}
