using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class TypeEntryNameTests
{
    [Fact]
    public void An_object_takes_its_name_from_its_own_engines_registration()
    {
        var first = new ModuleRegistry();
        first.Module("First").Type<Gadget>("FirstGadget");
        var second = new ModuleRegistry();
        second.Module("Second").Type<Gadget>("SecondGadget");
        using var firstEngine = new QmlEngine(first, new ManualEventLoop());
        using var secondEngine = new QmlEngine(second, new ManualEventLoop());

        Assert.Equal("FirstGadget", new Gadget { Engine = firstEngine }.TypeName);
        Assert.Equal("SecondGadget", new Gadget { Engine = secondEngine }.TypeName);
        Assert.Equal(nameof(Gadget), new Gadget().TypeName);
        var error = Assert.Throws<QmlException>(() => ValueConverter.Convert(1.0, typeof(Gadget), firstEngine));
        Assert.Contains("FirstGadget", error.Message, StringComparison.Ordinal);
    }

    private sealed class Gadget : QObject;
}
