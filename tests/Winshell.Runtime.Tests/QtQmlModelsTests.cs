namespace Winshell.Runtime.Tests;

/// <summary><c>QtQml.Models</c> exports the model types, and the <c>QtQml</c> objects stay in <c>QtQml</c>.</summary>
public class QtQmlModelsTests
{
    [Fact]
    public void Models_does_not_reexport_the_qtqml_objects()
    {
        var registry = StandardModules.Create();

        Assert.True(registry.Module("QtQml").Types.ContainsKey("QtObject"));
        Assert.True(registry.Module("QtQml").Types.ContainsKey("Timer"));
        Assert.True(registry.Module("QtQml.Models").Types.ContainsKey("ListModel"));
        Assert.True(registry.Module("QtQml.Models").Types.ContainsKey("ListElement"));
        Assert.False(registry.Module("QtQml.Models").Types.ContainsKey("QtObject"));
        Assert.False(registry.Module("QtQml.Models").Types.ContainsKey("Timer"));
        Assert.False(registry.Module("QtQml.Models").Types.ContainsKey("Component"));
    }
}
