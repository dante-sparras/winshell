namespace Winshell.Runtime.Tests;

public class CompletionErrorTests
{
    [Fact]
    public void A_failed_completion_names_the_exception_and_logs_its_stack()
    {
        using var shell = new TestShell(("shell.qml", "import Broken\nFailsToComplete {}"));
        shell.Log.MinimumLevel = LogLevel.Debug;
        var modules = StandardModules.Create();
        modules.Module("Broken").Type<FailsToComplete>("FailsToComplete");
        using var engine = new QmlEngine(modules, shell.Loop, shell.Log, shell.Directory);

        engine.CreateRoot(Path.Combine(shell.Directory, "shell.qml"));

        Assert.Contains(shell.Messages, m => m.Contains("Error scene: FailsToComplete: InvalidOperationException: completion failed", StringComparison.Ordinal));
        Assert.Contains(shell.Messages, m => m.StartsWith("Debug scene:", StringComparison.Ordinal) && m.Contains(nameof(FailsToComplete.ComponentComplete), StringComparison.Ordinal));
    }

    /// <summary>A type whose completion fails the way a runtime bug would.</summary>
    public sealed class FailsToComplete : QObject
    {
        public override void ComponentComplete() => throw new InvalidOperationException("completion failed");
    }
}
