using Winshell;

namespace MyShell;

/// <summary>The smallest shell: one panel along the bottom of the screen.</summary>
public sealed class AppShell : ShellConfiguration
{
    public override void Start(Shell shell)
    {
        _ = new PanelWindow
        {
            Anchors = Edges.Left | Edges.Right | Edges.Bottom,
            Height = 48,
            ReservedSize = 48,
            Content = new PanelView(),
            Visible = true
        };
    }
}
