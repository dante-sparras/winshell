using FluentAssertions;

namespace Winshell.Tests;

/// <summary>A Kit control reads its getter on every refresh and writes the screen only when the value moved.</summary>
[TestClass]
public sealed class PanelBindingTests
{
    [TestMethod]
    public void A_binding_writes_its_first_value_as_it_is_added()
    {
        var writes = new List<string>();
        var bindings = new PanelBindings();

        bindings.Add(new PanelBinding<string>(() => "On", writes.Add));

        writes.Should().Equal("On");
    }

    [TestMethod]
    public void A_refresh_writes_only_what_changed()
    {
        var level = 0.5;
        var name = "Speakers";
        var writes = new List<string>();
        var bindings = new PanelBindings();
        bindings.Add(new PanelBinding<double>(() => level, value => writes.Add("level " + value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        bindings.Add(new PanelBinding<string>(() => name, value => writes.Add("name " + value)));
        writes.Clear();

        bindings.Refresh().Should().Be(0);
        level = 0.7;

        bindings.Refresh().Should().Be(1);
        writes.Should().Equal("level 0.7");
    }

    [TestMethod]
    public void A_getter_that_throws_keeps_the_value_on_screen()
    {
        var fail = false;
        var writes = new List<int>();
        var binding = new PanelBinding<int>(() => fail ? throw new InvalidOperationException("gone") : 3, writes.Add);
        binding.Refresh();
        fail = true;

        binding.Refresh().Should().Be(0);
        binding.Shown.Should().Be(3);
        writes.Should().Equal(3);
    }

    [TestMethod]
    public void A_removed_group_stops_reading_its_getters()
    {
        var reads = 0;
        var panel = new PanelBindings();
        var item = new PanelBindings();
        item.Add(new PanelBinding<int>(() => ++reads, _ => { }));
        panel.Add(item);
        reads = 0;

        panel.Remove(item);
        panel.Refresh();

        reads.Should().Be(0);
    }

    [TestMethod]
    public void A_binding_added_during_a_refresh_is_read_from_the_next_one()
    {
        var reads = 0;
        var grow = false;
        var panel = new PanelBindings();
        panel.Add(new PanelBinding<bool>(() => grow, wanted =>
        {
            if (wanted)
            {
                panel.Add(new PanelBinding<int>(() => ++reads, _ => { }));
            }
        }));
        panel.Refresh();
        panel.Refresh();
        grow = true;

        panel.Refresh();
        reads.Should().Be(1);
        panel.Refresh();

        reads.Should().Be(2);
        panel.Count.Should().Be(2);
    }

    [TestMethod]
    public void A_cleared_group_reads_nothing()
    {
        var reads = 0;
        var panel = new PanelBindings();
        panel.Add(new PanelBinding<int>(() => ++reads, _ => { }));
        panel.Refresh();
        reads = 0;

        panel.Clear();
        panel.Refresh();

        reads.Should().Be(0);
    }

    [TestMethod]
    public void A_comparer_decides_what_counts_as_moved()
    {
        var text = "a";
        var writes = new List<string>();
        var binding = new PanelBinding<string>(() => text, writes.Add, StringComparer.OrdinalIgnoreCase);
        binding.Refresh();
        text = "A";

        binding.Refresh().Should().Be(0);
        writes.Should().Equal("a");
    }
}
