using Winshell.Platform.Windows.Services.WindowList;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class VirtualDesktopTests
{
    [Theory]
    [InlineData(22631u, false)]
    [InlineData(26099u, false)]
    [InlineData(26100u, true)]
    [InlineData(26200u, true)]
    [InlineData(26300u, true)]
    [InlineData(26399u, true)]
    [InlineData(26400u, false)]
    [InlineData(28000u, false)]
    public void Only_builds_26100_through_26399_use_the_desktop_method_table(uint build, bool supported)
    {
        var contract = DesktopContract.ForBuild(build);
        Assert.Equal(supported, contract.Supported);
        if (!supported)
            Assert.Contains($"{build}", contract.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_desktop_name_becomes_Desktop_N_and_a_stored_name_is_kept()
    {
        Assert.Equal("Desktop 1", VirtualDesktopRules.DisplayName("  ", 0));
        Assert.Equal("Desktop 2", VirtualDesktopRules.DisplayName(null, 1));
        Assert.Equal("Work", VirtualDesktopRules.DisplayName("  Work  ", 3));
        Assert.Equal("", VirtualDesktopRules.FormatId(Guid.Empty));
        Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", VirtualDesktopRules.FormatId(new Guid("00112233-4455-6677-8899-aabbccddeeff")));
    }

    [Fact]
    public void Removing_a_desktop_falls_back_to_the_previous_one_and_never_the_last()
    {
        Assert.Equal(1, VirtualDesktopRules.FallbackIndex(0, 3));
        Assert.Equal(0, VirtualDesktopRules.FallbackIndex(1, 3));
        Assert.Equal(1, VirtualDesktopRules.FallbackIndex(2, 3));
        Assert.Null(VirtualDesktopRules.FallbackIndex(0, 1));
        Assert.Null(VirtualDesktopRules.FallbackIndex(3, 3));
        Assert.Null(VirtualDesktopRules.FallbackIndex(-1, 2));
    }

    [Fact]
    public void The_list_names_desktops_and_marks_the_current_one()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var source = Source(first, ("", first), ("Work", second));
        source.Current = second;
        var desktops = new VirtualDesktopsService(null, source, new Log(), watch: false);

        Assert.True(desktops.Supported);
        Assert.Equal("", desktops.UnsupportedReason);
        Assert.Equal(2, desktops.Desktops.Rows.Count);
        var one = Assert.IsType<VirtualDesktop>(desktops.Desktops.Rows[0]);
        var two = Assert.IsType<VirtualDesktop>(desktops.Desktops.Rows[1]);
        Assert.Equal("Desktop 1", one.Name);
        Assert.Equal(0, one.Index);
        Assert.False(one.Current);
        Assert.Equal("Work", two.Name);
        Assert.Equal(1, two.Index);
        Assert.True(two.Current);
        Assert.Same(two, desktops.Current);
        Assert.Same(two, desktops.Find(second.ToString("D")));
    }

    [Fact]
    public void Switch_create_and_remove_change_the_list_and_the_last_desktop_stays()
    {
        var first = Guid.NewGuid();
        var source = Source(first, ("", first));
        var desktops = new VirtualDesktopsService(null, source, new Log(), watch: false);

        desktops.Remove(desktops.Current);
        Assert.DoesNotContain(source.Calls, call => call.StartsWith("remove:", StringComparison.Ordinal));
        Assert.Single(desktops.Desktops.Rows);

        var created = desktops.Create();
        Assert.NotNull(created);
        Assert.Equal(2, desktops.Desktops.Rows.Count);
        Assert.Equal("Desktop 2", created.Name);
        Assert.Contains(source.Calls, call => call.StartsWith("create:", StringComparison.Ordinal));

        desktops.SwitchTo(created);
        Assert.Same(created, desktops.Current);
        Assert.True(created.Current);
        Assert.False(((VirtualDesktop)desktops.Desktops.Rows[0]!).Current);

        desktops.Remove(created);
        Assert.Single(desktops.Desktops.Rows);
        Assert.Equal(first, source.Current);
        Assert.Contains(source.Calls, call => call == $"remove:{created.Id}:{first:D}");
    }

    [Fact]
    public void A_desktop_added_renamed_or_removed_outside_this_service_shows_up()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var source = Source(first, ("", first));
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(new ModuleRegistry(), loop);
        var desktops = new VirtualDesktopsService(engine, source, new Log(), watch: true);
        try
        {
            Assert.Single(desktops.Desktops.Rows);
            Assert.DoesNotContain(source.Calls, call => call is not "read");

            source.Items[0] = new DesktopRecord(first, "Work");
            loop.Advance(VirtualDesktopsService.RereadInterval);
            Assert.Equal("Work", Assert.IsType<VirtualDesktop>(desktops.Desktops.Rows[0]).Name);

            source.Items.Add(new DesktopRecord(second, "Extra"));
            loop.Advance(VirtualDesktopsService.RereadInterval);
            Assert.Equal(2, desktops.Desktops.Rows.Count);
            Assert.Equal("Extra", Assert.IsType<VirtualDesktop>(desktops.Desktops.Rows[1]).Name);

            source.Items.RemoveAll(item => item.Id == second);
            loop.Advance(VirtualDesktopsService.RereadInterval);
            Assert.Single(desktops.Desktops.Rows);
            Assert.Equal(first.ToString("D"), Assert.IsType<VirtualDesktop>(desktops.Desktops.Rows[0]).Id);
            Assert.DoesNotContain(source.Calls, call => call is not "read");
        }
        finally
        {
            desktops.Destroy();
        }
    }

    [Fact]
    public void An_unknown_build_does_not_read_or_change_desktops()
    {
        var source = Source(Guid.NewGuid());
        source.Contract = DesktopContract.ForBuild(22631);
        var desktops = new VirtualDesktopsService(null, source, new Log(), watch: false);

        Assert.False(desktops.Supported);
        Assert.Contains("22631", desktops.UnsupportedReason, StringComparison.Ordinal);
        Assert.Empty(desktops.Desktops.Rows);
        Assert.Empty(source.Calls);

        Assert.Null(desktops.Create());
        desktops.SwitchTo(new VirtualDesktop("00112233-4455-6677-8899-aabbccddeeff"));
        desktops.Remove(new VirtualDesktop("00112233-4455-6677-8899-aabbccddeeff"));
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void A_failed_read_blocks_changes_until_a_read_succeeds()
    {
        var id = Guid.NewGuid();
        var source = Source(id, ("", id));
        source.FailRead = true;
        var desktops = new VirtualDesktopsService(null, source, new Log(), watch: false);

        Assert.False(desktops.Supported);
        Assert.StartsWith("failed:", desktops.UnsupportedReason, StringComparison.Ordinal);
        Assert.Null(desktops.Create());
        Assert.DoesNotContain(source.Calls, call => call.StartsWith("create:", StringComparison.Ordinal));

        source.FailRead = false;
        desktops.Create();
        Assert.True(desktops.Supported);
        Assert.Equal(2, desktops.Desktops.Rows.Count);
    }

    [Fact]
    public void The_virtual_desktop_singleton_speaks_Windows_names()
    {
        var list = MetaType.Of(typeof(VirtualDesktopsService));
        Assert.True(list.Properties.ContainsKey("desktops"));
        Assert.True(list.Properties.ContainsKey("current"));
        Assert.True(list.Properties.ContainsKey("supported"));
        Assert.True(list.Properties.ContainsKey("unsupportedReason"));
        Assert.True(list.Methods.ContainsKey("switchTo"));
        Assert.True(list.Methods.ContainsKey("create"));
        Assert.True(list.Methods.ContainsKey("remove"));

        var desktop = MetaType.Of(typeof(VirtualDesktop));
        Assert.True(desktop.Properties.ContainsKey("id"));
        Assert.True(desktop.Properties.ContainsKey("name"));
        Assert.True(desktop.Properties.ContainsKey("index"));
        Assert.True(desktop.Properties.ContainsKey("current"));
        Assert.False(desktop.Properties["id"].CanWrite);
        Assert.False(desktop.Properties["name"].CanWrite);
    }

    [Fact]
    public void This_machine_reports_a_build_and_can_read_desktops_when_the_table_is_known()
    {
        var version = WindowsVersion.Read();
        Assert.True(version.Build > 0);
        var contract = DesktopContract.ForBuild(version.Build);
        if (!contract.Supported)
        {
            Assert.Contains($"{version.Build}", VirtualDesktopBroker.Shared.Contract.Reason, StringComparison.Ordinal);
            return;
        }

        var read = VirtualDesktopBroker.Shared.TryRead(out var desktops, out var current, out var error);
        if (!read)
        {
            Assert.StartsWith("unavailable:", error, StringComparison.Ordinal);
            return;
        }

        Assert.NotEmpty(desktops);
        Assert.Contains(desktops, desktop => desktop.Id == current);
        Assert.All(desktops, desktop => Assert.NotEqual(Guid.Empty, desktop.Id));
        var foreground = VirtualDesktopBroker.WindowDesktopId(NativeMethods.GetForegroundWindow());
        if (foreground.Length > 0)
            Assert.True(Guid.TryParse(foreground, out _));

        var service = new VirtualDesktopsService(null, VirtualDesktopBroker.Shared, new Log(), watch: false);
        Assert.True(service.Supported);
        Assert.Equal(desktops.Count, service.Desktops.Rows.Count);
        Assert.All(service.Desktops.Rows, row => Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<VirtualDesktop>(row).Name)));
    }

    private static FakeDesktops Source(Guid current, params (string Name, Guid Id)[] items)
    {
        var source = new FakeDesktops { Contract = DesktopContract.ForBuild(26300), Current = current };
        foreach (var (name, id) in items)
            source.Items.Add(new DesktopRecord(id, name));
        return source;
    }

    private sealed class FakeDesktops : IVirtualDesktopSource
    {
        public DesktopContract Contract { get; set; }
        public List<DesktopRecord> Items { get; } = [];
        public Guid Current { get; set; }
        public List<string> Calls { get; } = [];
        public bool FailRead { get; set; }

        public bool TryRead(out IReadOnlyList<DesktopRecord> desktops, out Guid current, out string error)
        {
            Calls.Add("read");
            if (FailRead)
            {
                desktops = [];
                current = Guid.Empty;
                error = "failed: boom";
                return false;
            }
            desktops = Items.ToArray();
            current = Current;
            error = "";
            return true;
        }

        public bool SwitchTo(Guid id)
        {
            Calls.Add($"switch:{id:D}");
            if (Items.All(item => item.Id != id))
                return false;
            Current = id;
            return true;
        }

        public Guid? Create()
        {
            var id = Guid.NewGuid();
            Items.Add(new DesktopRecord(id, ""));
            Calls.Add($"create:{id:D}");
            return id;
        }

        public bool Remove(Guid id, Guid fallback)
        {
            Calls.Add($"remove:{id:D}:{fallback:D}");
            Items.RemoveAll(item => item.Id == id);
            if (Current == id)
                Current = fallback;
            return true;
        }
    }
}
