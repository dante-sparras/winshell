using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Winshell;

/// <summary>
/// The base of what a service exposes. A change is raised on the thread that made it, which for a service is its
/// event loop, so XAML bindings and Reactor can read it.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets a field and reports the change when the value differs.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Changed(property);
        return true;
    }

    protected void Changed([CallerMemberName] string property = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>Reports a change of a property read from a snapshot, when the two snapshots differ.</summary>
    protected void Changed(bool changed, string property)
    {
        if (changed)
            Changed(property);
    }
}
