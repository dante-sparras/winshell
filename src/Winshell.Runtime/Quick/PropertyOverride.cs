namespace Winshell.Runtime.Quick;

/// <summary>
/// Takes a property's binding (or plain value) off the target and puts it back.
/// States and the Binding element share this so an override wins until it is lifted.
/// </summary>
internal static class PropertyOverride
{
    internal sealed class Slot
    {
        public Binding? Binding { get; init; }

        public object? Value { get; init; }

        public bool HadBinding { get; init; }
    }

    public static Slot Capture(QObject target, string property)
    {
        var binding = target.TakeBinding(property);
        object? value = null;
        if (binding is null)
        {
            using (DependencyTracker.Suspend())
                value = target.ReadProperty(property);
        }
        return new Slot { Binding = binding, Value = value, HadBinding = binding is not null };
    }

    public static void Restore(QObject target, string property, Slot slot)
    {
        if (target.IsDestroyed)
            return;
        if (slot.HadBinding && slot.Binding is { } binding)
        {
            target.SetBinding(property, binding);
            binding.Evaluate();
            return;
        }
        Write(target, property, slot.Value);
    }

    /// <summary>Installs a live binding. Evaluating it writes through <see cref="QObject.WriteProperty"/>, so a Behavior runs.</summary>
    public static void InstallLive(QObject target, string property, Func<object?> evaluate, string description)
    {
        if (target.IsDestroyed)
            return;
        var binding = new Binding(target, property, evaluate, description);
        target.SetBinding(property, binding);
        binding.Evaluate();
    }

    public static void Write(QObject target, string property, object? value)
    {
        if (target.IsDestroyed)
            return;
        try
        {
            target.WriteProperty(property, value);
        }
        catch (QmlException e)
        {
            (target.Engine?.Log ?? Log.Default).Warn("scene", $"Cannot assign to {target.TypeName}.{property}: {e.Message}");
        }
    }
}
