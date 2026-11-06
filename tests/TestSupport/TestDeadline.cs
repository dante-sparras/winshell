using System.Collections.Concurrent;
using System.Reflection;
using Xunit.v3;

[assembly: TestDeadline]

namespace Winshell.TestSupport;

/// <summary>
/// Cancels <c>TestContext.Current.CancellationToken</c> when a test has run for two minutes, so a test that passes the
/// token to what it waits on fails by itself and the run goes on. A <c>Timeout</c> longer than that is the limit instead,
/// which is how the packed-shell test outlasts the usual deadline. xUnit has no default for <c>Timeout</c>, and a class
/// derived from <c>FactAttribute</c> hides its tests from the xUnit analyzers. A test blocked in something that takes no
/// token runs on until the run's hang timeout, unless it sets <c>Timeout</c> itself.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class TestDeadlineAttribute : BeforeAfterTestAttribute
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, Timer> Deadlines = new();

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var context = TestContext.Current;
        var timeout = methodUnderTest.GetCustomAttribute<FactAttribute>()?.Timeout ?? 0;
        var limit = timeout > Limit.TotalMilliseconds ? TimeSpan.FromMilliseconds(timeout) : Limit;
        Deadlines[test.UniqueID] = new Timer(_ => context.CancelCurrentTest(), null, limit, Timeout.InfiniteTimeSpan);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (Deadlines.TryRemove(test.UniqueID, out var deadline))
            deadline.Dispose();
    }
}
