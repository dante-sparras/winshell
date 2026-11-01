using System.Diagnostics;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A log file reads the same as its text, and a diagnostics run finishes even when the doctor throws.</summary>
public sealed class DoctorResourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-doctor-resources", Guid.NewGuid().ToString("N"));

    public DoctorResourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void A_log_file_agrees_with_the_last_load_in_its_text()
    {
        const string Failed = "08:00:01.000 ERROR scene: Failed to load configuration: C:\\c\\shell.qml[7:5]: Cannot assign\r\n";
        const string Reloaded = "08:00:02.000  INFO: Configuration Loaded\r\n";
        const string Refailed = "08:00:03.000 ERROR scene: Failed to reload configuration: C:\\c\\Bar.qml[1:1]: x\r\n";
        const string Launched = "08:00:04.000  INFO: Launching config: \"C:\\c\\shell.qml\"\r\n";
        const string Named = "12:00:00.000 ERROR scene: Failed to load configuration: böom\n";

        Assert.Equal(DoctorProbe.LastLoadErrorIn(Failed), LoadError(Failed));
        Assert.Equal(DoctorProbe.LastLoadErrorIn(Failed + Reloaded), LoadError(Failed + Reloaded));
        Assert.Equal(DoctorProbe.LastLoadErrorIn(Failed + Reloaded + Refailed), LoadError(Failed + Reloaded + Refailed));
        Assert.Equal(DoctorProbe.LastLoadErrorIn(Failed + Launched), LoadError(Failed + Launched));
        Assert.Equal("böom", LoadError(Named));
        Assert.Null(LoadError(""));
    }

    [Fact]
    public void A_diagnostics_run_that_runs_out_of_memory_finishes()
    {
        var doctor = new OutOfMemoryDoctor();
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        engine.AddService<IDoctor>(doctor);
        var diagnostics = new DiagnosticsService(engine);

        try
        {
            diagnostics.Run();
            WaitUntilIdle(diagnostics, loop);

            Assert.False(diagnostics.Running);
            Assert.Equal(1, doctor.Runs);
            Assert.Equal("0 passed, 0 warnings, 1 failed.", diagnostics.Summary);
            var row = Assert.IsType<DiagnosticCheck>(Assert.Single(diagnostics.Checks.Rows));
            Assert.Equal("Could not run: OutOfMemoryException: doctor", row.Message);
            Assert.Equal("fail", row.Status);

            diagnostics.Run();
            WaitUntilIdle(diagnostics, loop);
            Assert.False(diagnostics.Running);
            Assert.Equal(2, doctor.Runs);
        }
        finally
        {
            diagnostics.Destroy();
            loop.Process();
        }
    }

    private string? LoadError(string text)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text);
        return DoctorProbe.LastLoadError(path);
    }

    private static void WaitUntilIdle(DiagnosticsService diagnostics, ManualEventLoop loop) =>
        Wait.Until(() => !diagnostics.Running, "the run to finish", loop.Process);

    private sealed class OutOfMemoryDoctor : IDoctor
    {
        public int Runs { get; private set; }

        public IReadOnlyList<DoctorCheck> Run()
        {
            Runs++;
            throw new OutOfMemoryException("doctor");
        }
    }
}
