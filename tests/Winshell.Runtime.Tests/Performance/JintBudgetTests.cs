using System.Diagnostics;
using System.Globalization;

namespace Winshell.Runtime.Tests.Performance;

/// <summary>
/// What starting an engine, evaluating bindings and loading a typical shell may cost. Times are the median of
/// several warm runs, and their budgets are about 3.5 times what CI's windows-latest runner measured, so a slow
/// runner passes and a clear regression fails. Allocations and compile counts barely vary between machines, so their
/// budgets are about 1.5 times the measurement, and exact. check.ps1 runs these alone, after the other tests, so
/// nothing competes with them for the CPU. Raise a budget deliberately, with the reason in the PR.
/// </summary>
[Trait("Category", "Performance")]
[Collection(nameof(JintBudgetTests))]
[CollectionDefinition(nameof(JintBudgetTests), DisableParallelization = true)]
public class JintBudgetTests(ITestOutputHelper output)
{
    private const int Runs = 7;

    [Fact]
    public void Starting_an_engine()
    {
        using var shell = new TestShell(("shell.qml", "import QtQml\nQtObject {}\n"));
        var path = Path.Combine(shell.Directory, "shell.qml");
        var result = Measure(() =>
        {
            using var engine = new QmlEngine(StandardModules.Create(), shell.Loop, shell.Log, shell.Directory);
            engine.CreateRoot(path);
        });
        Report("start an engine", result, "ms");
        // CI: 1.1 ms, 269 KB.
        Assert.InRange(result.Milliseconds, 0, 4);
        Assert.InRange(result.AllocatedBytes, 0, 400_000);
    }

    [Fact]
    public void Evaluating_bindings()
    {
        const int Writes = 50;
        const int Delegates = 200;
        var root = TestShell.Run($$"""
            import QtQuick
            Item {
              id: root
              property int tick: 0
              Repeater {
                model: {{Delegates}}
                Item {
                  required property int index
                  property int a: root.tick + index
                  property int b: a * 2
                  property string label: "item " + b
                  width: b % 50
                }
              }
            }
            """, out var shell);
        using var _ = shell;
        var tick = 0;
        var result = Measure(() =>
        {
            for (var i = 0; i < Writes; i++)
                root.WriteProperty("tick", ++tick);
        });
        // Each write re-evaluates four bindings in every delegate.
        const int Evaluations = Writes * Delegates * 4;
        // Microseconds and bytes for one evaluation.
        var perEvaluation = new Result(result.Milliseconds * 1000 / Evaluations, result.AllocatedBytes / Evaluations);
        Report("evaluate one binding", perEvaluation, "µs");
        Assert.Empty(shell.Messages);
        // CI: 5.7 µs, 640 bytes.
        Assert.InRange(perEvaluation.Milliseconds, 0, 20);
        Assert.InRange(perEvaluation.AllocatedBytes, 0, 1_000);
    }

    [Fact]
    public void Loading_a_typical_shell()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Performance", "typical-shell", "shell.qml");
        var messages = new List<string>();
        var log = new Log { Sink = (level, category, message) => messages.Add($"{level} {category}: {message}") };
        var loop = new ManualEventLoop { Log = log };
        var compiled = 0;
        var result = Measure(() =>
        {
            using var engine = new QmlEngine(StandardModules.Create(), loop, log, Path.GetDirectoryName(path));
            engine.CreateRoot(path);
            loop.Process();
            compiled = engine.Js.CompiledFunctionCount;
        });
        Report("load the typical shell", result, "ms");
        output.WriteLine($"compiled functions: {compiled}");
        Assert.Empty(messages);
        // CI: 83 ms, 9.4 MB, 38 functions.
        Assert.InRange(result.Milliseconds, 0, 300);
        Assert.InRange(result.AllocatedBytes, 0, 14_000_000);
        // Code compiles once per place it is written, however many screens and delegates run it.
        Assert.InRange(compiled, 0, 38);
    }

    private sealed record Result(double Milliseconds, long AllocatedBytes);

    /// <summary>Runs once to warm up, then <see cref="Runs"/> times: the median time and the least allocated.</summary>
    private static Result Measure(Action work)
    {
        work();
        var times = new List<double>();
        var allocated = long.MaxValue;
        for (var i = 0; i < Runs; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            work();
            watch.Stop();
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return new Result(times[Runs / 2], allocated);
    }

    private void Report(string what, Result result, string unit) =>
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {result.Milliseconds:0.###} {unit}, {result.AllocatedBytes:N0} bytes"));
}
