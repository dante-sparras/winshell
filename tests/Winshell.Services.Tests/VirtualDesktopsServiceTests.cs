using Winshell.Windows;

namespace Winshell.Services.Tests;

public sealed class VirtualDesktopsServiceTests
{
    private readonly ManualEventLoop _loop = new();

    private VirtualDesktopsService Start(FakeDesktops source) => new(_loop, new Log(), source);

    [Fact]
    public void The_list_names_desktops_and_marks_the_current_one()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var source = FakeDesktops.Holding(second, first);
        source.Items.Add(new DesktopRecord(second, "Work"));
        using var desktops = Start(source);

        Assert.True(desktops.Supported);
        Assert.Equal("", desktops.UnsupportedReason);
        Assert.Equal(["Desktop 1", "Work"], desktops.Desktops.Select(desktop => desktop.Name));
        Assert.Equal([0, 1], desktops.Desktops.Select(desktop => desktop.Index));
        Assert.Equal([false, true], desktops.Desktops.Select(desktop => desktop.Current));
        Assert.Same(desktops.Desktops[1], desktops.Current);
        Assert.Same(desktops.Desktops[1], desktops.Find(second.ToString("D")));
    }

    [Fact]
    public void Switch_create_and_remove_change_the_list_and_the_last_desktop_stays()
    {
        var first = Guid.NewGuid();
        var source = FakeDesktops.Holding(first, first);
        using var desktops = Start(source);

        desktops.Remove(desktops.Current);
        Assert.DoesNotContain(source.Calls, call => call.StartsWith("remove:", StringComparison.Ordinal));
        Assert.Single(desktops.Desktops);

        var created = desktops.Create();
        Assert.NotNull(created);
        Assert.Equal(2, desktops.Desktops.Count);
        Assert.Equal("Desktop 2", created.Name);

        desktops.SwitchTo(created);
        Assert.Same(created, desktops.Current);
        Assert.True(created.Current);
        Assert.False(desktops.Desktops[0].Current);

        desktops.Remove(created);
        Assert.Single(desktops.Desktops);
        Assert.Equal(first, source.Current);
        Assert.Contains($"remove:{created.Id}:{first:D}", source.Calls);
    }

    [Fact]
    public void A_desktop_that_stays_is_the_same_object_and_moves_with_the_list()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var source = FakeDesktops.Holding(first, first, second);
        using var desktops = Start(source);
        var kept = desktops.Desktops[1];

        source.Items.RemoveAt(0);
        source.Current = second;
        _loop.Advance(VirtualDesktopsService.RereadInterval);

        Assert.Same(kept, Assert.Single(desktops.Desktops));
        Assert.Equal(0, kept.Index);
        Assert.Equal("Desktop 1", kept.Name);
        Assert.Same(kept, desktops.Current);
    }

    [Fact]
    public void A_desktop_added_or_removed_elsewhere_shows_up_and_nothing_else_reads_again()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var source = FakeDesktops.Holding(first, first);
        using var desktops = Start(source);
        var reads = source.Calls.Count;

        _loop.Advance(VirtualDesktopsService.RereadInterval);
        Assert.Equal(reads, source.Calls.Count);

        source.Items.Add(new DesktopRecord(second, "Extra"));
        _loop.Advance(VirtualDesktopsService.RereadInterval);
        Assert.Equal("Extra", desktops.Desktops[1].Name);

        source.Items.RemoveAll(item => item.Id == second);
        _loop.Advance(VirtualDesktopsService.RereadInterval);
        Assert.Single(desktops.Desktops);
        Assert.DoesNotContain(source.Calls, call => call is not "read");
    }

    [Fact]
    public void An_unknown_build_does_not_read_or_change_desktops_or_poll()
    {
        var source = FakeDesktops.Holding(Guid.NewGuid());
        source.Contract = DesktopContract.ForBuild(22631);
        using var desktops = Start(source);

        Assert.False(desktops.Supported);
        Assert.Contains("22631", desktops.UnsupportedReason, StringComparison.Ordinal);
        Assert.Empty(desktops.Desktops);
        Assert.Null(desktops.Current);
        Assert.Null(_loop.NextDue);

        Assert.Null(desktops.Create());
        desktops.SwitchTo(new VirtualDesktop("00112233-4455-6677-8899-aabbccddeeff"));
        desktops.Remove(new VirtualDesktop("00112233-4455-6677-8899-aabbccddeeff"));
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void A_failed_read_blocks_changes_until_a_read_succeeds()
    {
        var id = Guid.NewGuid();
        var source = FakeDesktops.Holding(id, id);
        source.FailRead = true;
        using var desktops = Start(source);

        Assert.False(desktops.Supported);
        Assert.StartsWith("failed:", desktops.UnsupportedReason, StringComparison.Ordinal);
        Assert.Null(desktops.Create());
        Assert.DoesNotContain(source.Calls, call => call.StartsWith("create:", StringComparison.Ordinal));

        source.FailRead = false;
        desktops.Create();

        Assert.True(desktops.Supported);
        Assert.Equal("", desktops.UnsupportedReason);
        Assert.Equal(2, desktops.Desktops.Count);
    }

    [Fact]
    public void A_failed_read_after_a_good_one_keeps_the_list()
    {
        var id = Guid.NewGuid();
        var source = FakeDesktops.Holding(id, id);
        using var desktops = Start(source);

        source.FailRead = true;
        desktops.SwitchTo(desktops.Current);

        Assert.True(desktops.Supported);
        Assert.Single(desktops.Desktops);
    }

    [Fact]
    public void A_disposed_service_stops_reading()
    {
        var id = Guid.NewGuid();
        var source = FakeDesktops.Holding(id, id);
        var desktops = Start(source);
        var reads = source.Calls.Count;

        desktops.Dispose();
        source.Items.Clear();
        _loop.Advance(VirtualDesktopsService.RereadInterval);

        Assert.Equal(reads, source.Calls.Count);
        Assert.Null(_loop.NextDue);
    }
}
