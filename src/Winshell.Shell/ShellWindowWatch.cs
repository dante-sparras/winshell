using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace Winshell;

/// <summary>The shell window an element is drawn in, and its property changes while the element is in the tree.</summary>
internal sealed class ShellWindowWatch(PropertyChangedEventHandler changed)
{
    public ShellWindow? Window { get; private set; }

    /// <summary>Watches the window <paramref name="element"/> is drawn in now, and stops watching any other.</summary>
    public void Attach(UIElement element)
    {
        Detach();
        Window = ShellWindow.Of(element);
        if (Window is not null)
            Window.PropertyChanged += changed;
    }

    public void Detach()
    {
        if (Window is null)
            return;
        Window.PropertyChanged -= changed;
        Window = null;
    }
}
