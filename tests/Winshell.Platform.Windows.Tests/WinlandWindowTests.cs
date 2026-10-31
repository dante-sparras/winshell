using Winshell.Platform.Windows.Services;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class WinlandWindowTests
{
    [Fact]
    public void WinlandWindow_comes_from_the_list_and_is_not_attached()
    {
        var registry = new ModuleRegistry();
        WindowManagerModules.Register(registry);

        Assert.True(registry.TryGetModule("Winshell.Winland", out var module));
        var type = module.Types["WinlandWindow"];
        Assert.Null(type.Attached);
        Assert.Null(type.AttachedType);
        Assert.Equal("WinlandWindow comes from Winland.toplevels", type.UncreatableReason);
    }
}
