using Microsoft.UI.Xaml;

namespace Winshell.DefaultShell;

/// <summary>The default shell: what runs when no configuration is found.</summary>
public sealed class DefaultShellConfiguration : ShellConfiguration
{
    private readonly List<PanelWindow> _bars = [];

    public override void Start(Shell shell)
    {
        foreach (var screen in shell.Screens)
        {
            var bar = new PanelWindow
            {
                Screen = screen,
                Anchors = Edges.Top | Edges.Left | Edges.Right,
                Height = 48,
                ExclusiveZone = 48,
                Backdrop = WindowBackdrop.Acrylic,
                Content = new Hello(),
            };
            bar.Visible = true;
            _bars.Add(bar);
        }
    }
}
