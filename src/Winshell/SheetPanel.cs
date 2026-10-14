using Microsoft.UI.Xaml;
using ShellKit = Winshell.Kit.Kit;

namespace Winshell;

/// <summary>A sheet plugin's panel, drawn with the Kit. An author writes only the manifest.</summary>
internal sealed class SheetPanel : SheetPlugin, IKitPanel
{
    private long _built = -1;
    private ShellKit? _kit;

    public SheetPanel(PluginManifest manifest)
        : base(manifest)
    {
    }

    public KitPanel BuildPanel(ShellKit kit)
    {
        _kit = kit;
        _built = Revision;
        var rows = new List<UIElement>();
        if (Sheet.Title.Length > 0)
        {
            rows.Add(kit.PanelTitle(Sheet.Title));
            rows.Add(kit.PanelSeparator());
        }

        foreach (var row in Sheet.Rows)
        {
            rows.Add(row switch
            {
                SheetRow.Text text => kit.Text(text.Value),
                SheetRow.Action action => kit.ListItem(action.Label, action.Command, () => OnPanel(action.Command, null)),
                SheetRow.Section section => kit.PanelSectionHeader(section.Label),
                _ => kit.PanelSeparator(),
            });
        }

        return kit.Panel(rows.ToArray());
    }

    public void UpdatePanel()
    {
        if (_built >= 0 && Revision != _built)
        {
            _built = Revision;
            _kit?.Rebuild();
        }
    }
}
