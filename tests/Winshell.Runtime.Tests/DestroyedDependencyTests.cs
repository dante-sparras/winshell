using System.Runtime.CompilerServices;

namespace Winshell.Runtime.Tests;

public class DestroyedDependencyTests
{
    [Fact]
    public void A_live_binding_releases_a_destroyed_dependency_without_running_again()
    {
        var target = new QObject();
        var (binding, source) = Observe(target);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(source.IsAlive);
        GC.KeepAlive(binding);
        target.Destroy();
    }

    [Fact]
    public void A_dependency_destroyed_during_evaluation_is_not_retained()
    {
        var target = new QObject();
        var (binding, source) = Observe(target, destroyDuringRead: true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(source.IsAlive);
        GC.KeepAlive(binding);
        target.Destroy();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Binding Binding, WeakReference Source) Observe(QObject target, bool destroyDuringRead = false)
    {
        var source = new QObject { ObjectName = "source" };
        var weak = new WeakReference(source);
        var binding = new Binding(target, "objectName", () =>
        {
            var current = (QObject)weak.Target!;
            var name = current.ObjectName;
            if (destroyDuringRead) current.Destroy();
            return name;
        }, "dependency lifetime");
        binding.Evaluate();
        source.Destroy();
        return (binding, weak);
    }
}
