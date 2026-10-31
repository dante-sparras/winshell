using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using Winshell.Platform.Windows.Diagnostics;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The doctor reads this PC without keeping what it opened, and a diagnostics run finishes even when the doctor
/// throws. Nothing here writes the real registry or shell.
/// </summary>
public sealed class DoctorResourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winshell-doctor-resources", Guid.NewGuid().ToString("N"));

    public DoctorResourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Reading_the_Windows_version_closes_its_registry_keys()
    {
        using var direct = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var revision = direct?.GetValue("UBR") is int value ? value : 0;

        lock (DoctorProbe.VersionKeyGate)
        {
            var real = DoctorProbe.OpenVersionKey;
            try
            {
                ClosesTheKey(() => Assert.Equal(revision, DoctorProbe.WindowsUpdateRevision()));
                ClosesTheKey(() => _ = DoctorProbe.WindowsServer());
                ClosesTheKey(() => _ = DoctorProbe.WindowsDisplayVersion());
            }
            finally
            {
                DoctorProbe.OpenVersionKey = real;
            }
        }
    }

    private static void ClosesTheKey(Action read)
    {
        RegistryKey? opened = null;
        DoctorProbe.OpenVersionKey = () => opened = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        read();
        Assert.NotNull(opened);
        Assert.Throws<ObjectDisposedException>(() => _ = opened.Handle);
    }

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
    public void The_last_load_in_a_long_log_is_read_without_holding_the_file()
    {
        var warm = Path.Combine(_dir, "warm.txt");
        File.WriteAllText(warm, "12:00:00.000 ERROR scene: Failed to load configuration: warm\n");
        Assert.Equal("warm", DoctorProbe.LastLoadError(warm));

        var path = Path.Combine(_dir, "long.txt");
        WriteFiller(path, 20_000, "12:00:00.000 ERROR scene: Failed to load configuration: boom\n");
        var size = new FileInfo(path).Length;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = DoctorProbe.LastLoadError(path);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal("boom", error);
        Assert.True(size > 512 * 1024);
        Assert.True(allocated < 512 * 1024, $"reading the last load allocated {allocated} bytes of a {size}-byte log");
    }

    [Fact]
    public void An_earlier_load_error_still_counts_when_the_rest_of_a_long_log_says_nothing()
    {
        var path = Path.Combine(_dir, "early.txt");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(Encoding.UTF8.GetBytes("12:00:00.000 ERROR scene: Failed to load configuration: early\n"));
            WriteFiller(stream, 8_000);
        }

        Assert.Equal("early", DoctorProbe.LastLoadError(path));
    }

    [Fact]
    public void A_later_successful_load_in_a_long_log_clears_the_error()
    {
        var path = Path.Combine(_dir, "reloaded.txt");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(Encoding.UTF8.GetBytes("12:00:00.000 ERROR scene: Failed to load configuration: gone\n"));
            WriteFiller(stream, 8_000);
            stream.Write(Encoding.UTF8.GetBytes("12:00:01.000  INFO: Configuration Loaded\n"));
        }

        Assert.Null(DoctorProbe.LastLoadError(path));
    }

    [Fact]
    public void A_load_error_on_a_line_longer_than_one_read_is_kept()
    {
        var path = Path.Combine(_dir, "wide.txt");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(Encoding.UTF8.GetBytes("11:00:00.000  INFO: Launching config: \"C:\\c\\shell.qml\"\n"));
            stream.Write(Encoding.UTF8.GetBytes("12:00:00.000 ERROR scene: Failed to load configuration: wide-boom"));
            var pad = new byte[64 * 1024];
            Array.Fill(pad, (byte)' ');
            stream.Write(pad);
            stream.WriteByte((byte)'\n');
        }

        var error = DoctorProbe.LastLoadError(path);
        Assert.NotNull(error);
        Assert.StartsWith("wide-boom", error, StringComparison.Ordinal);
        Assert.Equal("wide-boom".Length + (64 * 1024), error.Length);
    }

    [Fact]
    public void A_character_split_across_a_read_stays_in_the_load_error()
    {
        var marker = Encoding.UTF8.GetBytes("12:00:00.000 ERROR scene: Failed to load configuration: b");
        var umlaut = new byte[] { 0xC3, 0xB6 };
        var rest = Encoding.UTF8.GetBytes("om");
        var padLength = DoctorProbe.LogChunk + 1 - (umlaut.Length + rest.Length + 1);
        var path = Path.Combine(_dir, "split.txt");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(marker);
            stream.Write(umlaut);
            stream.Write(rest);
            var pad = new byte[padLength];
            Array.Fill(pad, (byte)' ');
            stream.Write(pad);
            stream.WriteByte((byte)'\n');
        }

        var error = DoctorProbe.LastLoadError(path);
        Assert.NotNull(error);
        Assert.StartsWith("böom", error, StringComparison.Ordinal);
        Assert.Equal("böom".Length + padLength, error.Length);
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

    private static void WriteFiller(string path, int lines, string tail)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteFiller(stream, lines);
        stream.Write(Encoding.UTF8.GetBytes(tail));
    }

    private static void WriteFiller(Stream stream, int lines)
    {
        var line = Encoding.ASCII.GetBytes(new string('a', 80) + "\n");
        for (var i = 0; i < lines; i++)
            stream.Write(line);
    }

    private static void WaitUntilIdle(DiagnosticsService diagnostics, ManualEventLoop loop)
    {
        var watch = Stopwatch.StartNew();
        while (diagnostics.Running && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(10);
            loop.Process();
        }
    }

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
