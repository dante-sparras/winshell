namespace Winshell.Runtime.Tests;

public class EmptyComponentTests
{
    [Fact]
    public void Create_object_on_a_component_with_no_engine_returns_null()
    {
        var component = new QmlComponent();

        Assert.Null(component.CreateObject());
    }
}
