using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class PanelReloadTests
{
    public sealed class Edited : Grid
    {
        public static int Built { get; private set; }

        public Edited() => Built++;

        public Func<int> Counter()
        {
            var seen = Built;
            return () => seen;
        }
    }

    public sealed class Throws : Grid
    {
        public static bool Fail { get; set; }

        public Throws()
        {
            if (Fail)
                throw new InvalidOperationException("half typed");
        }
    }

    public sealed class Takes(int size) : Grid
    {
        public int Size { get; } = size;
    }

    public sealed class Disposable : Grid, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private static PanelWindow Panel(Microsoft.UI.Xaml.UIElement content) => new()
    {
        Anchors = Edges.Top | Edges.Left,
        Width = 200,
        Height = 40,
        Content = content,
        Visible = true,
    };

    [Fact]
    public async Task An_edited_panel_gets_new_content_and_the_others_keep_theirs()
    {
        var (replaced, same, kept) = await ShellFixture.On(shell =>
        {
            var edited = new Edited();
            var other = new Grid();
            using var editedPanel = Panel(edited);
            using var otherPanel = Panel(other);
            var handle = editedPanel.Host!.Handle;

            PanelReload.Apply(shell, [typeof(Edited)]);

            return (editedPanel.Content is Edited fresh && !ReferenceEquals(fresh, edited), editedPanel.Host!.Handle == handle, ReferenceEquals(otherPanel.Content, other));
        });

        Assert.True(replaced);
        Assert.True(same, "The window stays. Only its content is new.");
        Assert.True(kept);
    }

    [Fact]
    public async Task An_edit_to_a_closure_counts_as_an_edit_to_its_type()
    {
        var replaced = await ShellFixture.On(shell =>
        {
            var edited = new Edited();
            using var panel = Panel(edited);

            var closure = typeof(Edited).GetNestedTypes(BindingFlags.NonPublic).Single(type => type.Name.StartsWith('<'));
            PanelReload.Apply(shell, [closure]);

            return !ReferenceEquals(panel.Content, edited);
        });

        Assert.True(replaced);
    }

    [Fact]
    public async Task A_panel_that_shows_an_edited_control_gets_new_content()
    {
        var built = await ShellFixture.On(async shell =>
        {
            var inner = new Edited();
            var root = new NestedRoot(inner);
            using var panel = Panel(root);
            await ShellFixture.LaidOut(inner);

            PanelReload.Apply(shell, [typeof(Edited)]);

            return !ReferenceEquals(panel.Content, root);
        });

        Assert.True(built);
    }

    public sealed class NestedRoot : Grid
    {
        public NestedRoot()
        {
        }

        public NestedRoot(Edited child) => Children.Add(child);
    }

    [Fact]
    public async Task Content_that_throws_while_it_is_typed_stays_as_it_was()
    {
        var kept = await ShellFixture.On(shell =>
        {
            var content = new Throws();
            using var panel = Panel(content);
            Throws.Fail = true;
            try
            {
                PanelReload.Apply(shell, [typeof(Throws)]);
            }
            finally
            {
                Throws.Fail = false;
            }
            return ReferenceEquals(panel.Content, content);
        });

        Assert.True(kept);
    }

    [Fact]
    public async Task Content_that_needs_arguments_stays_as_it_was()
    {
        var kept = await ShellFixture.On(shell =>
        {
            var content = new Takes(3);
            using var panel = Panel(content);

            PanelReload.Apply(shell, [typeof(Takes)]);

            return ReferenceEquals(panel.Content, content);
        });

        Assert.True(kept);
    }

    [Fact]
    public async Task The_content_it_replaces_is_disposed()
    {
        var disposed = await ShellFixture.On(shell =>
        {
            var content = new Disposable();
            using var panel = Panel(content);

            PanelReload.Apply(shell, [typeof(Disposable)]);

            return content.Disposed;
        });

        Assert.True(disposed);
    }
}
