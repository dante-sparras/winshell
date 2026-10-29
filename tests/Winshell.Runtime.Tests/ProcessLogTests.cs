namespace Winshell.Runtime.Tests;

public class ProcessLogTests
{
    [Fact]
    public void A_binding_error_without_an_engine_reaches_the_process_log()
    {
        var seen = new List<string>();
        var previousSink = Log.Default.Sink;
        var previousLevel = Log.Default.MinimumLevel;
        Log.Default.MinimumLevel = LogLevel.Warn;
        Log.Default.Sink = (_, _, message) => seen.Add(message);
        try
        {
            var obj = new QObject();
            var binding = new Binding(obj, "objectName", () => throw new InvalidOperationException("process-log-marker"), "process-log-marker.qml");
            binding.Evaluate();
            Assert.Contains(seen, message => message.Contains("process-log-marker", StringComparison.Ordinal));
        }
        finally
        {
            Log.Default.Sink = previousSink;
            Log.Default.MinimumLevel = previousLevel;
        }
    }
}
