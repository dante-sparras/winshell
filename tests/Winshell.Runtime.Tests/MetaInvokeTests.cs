namespace Winshell.Runtime.Tests;

public class MetaInvokeTests
{
    [Fact]
    public void A_thrown_method_keeps_the_stack_of_the_method()
    {
        var obj = new Sample();
        var error = Assert.Throws<InvalidOperationException>(() => MetaType.Of(typeof(Sample)).Methods["boom"].Invoke(obj, []));
        Assert.Contains(nameof(Sample.Boom), error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_argument_is_filled_from_the_cached_signature()
    {
        var obj = new Sample();
        var value = MetaType.Of(typeof(Sample)).Methods["add"].Invoke(obj, [2]);
        Assert.Equal(3, value);
    }

    private sealed class Sample : QObject
    {
        [Qml]
        public void Boom() => throw new InvalidOperationException("boom");

        [Qml]
        public int Add(int value, int extra = 1) => value + extra;
    }
}
