using System.Runtime.CompilerServices;

namespace Winshell.Runtime.Tests;

public class TypeNameLifetimeTests
{
    [Fact]
    public void A_shared_native_type_does_not_keep_the_first_scope_object_alive()
    {
        using var shell = new TestShell(("shell.qml", "import QtQuick\nItem {}"));
        var imports = shell.Engine.ImportsOf(shell.Engine.LoadDocument(Path.Combine(shell.Directory, "shell.qml")));
        var scope = ResolveType(imports);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(scope.IsAlive);
        GC.KeepAlive(imports);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ResolveType(ImportScope imports)
    {
        var scope = new QObject();
        Assert.True(imports.TryResolveName("Text", scope, null, out _));
        scope.Destroy();
        return new WeakReference(scope);
    }
}
