using System.ComponentModel;
using Winshell.Network;

namespace Winshell.Services.Tests;

public sealed class NetworkingServiceTests
{
    private const string Secret = "winshell-profile-key-9f3a";

    private const string SavedXml = """
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<name>Office</name>
        	<SSIDConfig>
        		<SSID>
        			<name>Cafe</name>
        		</SSID>
        		<nonBroadcast>true</nonBroadcast>
        	</SSIDConfig>
        	<connectionMode>auto</connectionMode>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>WPA2PSK</authentication>
        				<encryption>AES</encryption>
        			</authEncryption>
        			<sharedKey>
        				<keyType>passPhrase</keyType>
        				<protected>true</protected>
        				<keyMaterial>winshell-profile-key-9f3a</keyMaterial>
        			</sharedKey>
        		</security>
        	</MSM>
        </WLANProfile>
        """;

    private static readonly Guid AdapterId = Guid.Parse("0b0f2811-87b5-48d6-9d7a-7a26ae12ff33");

    private static NetworkInfo Wifi(string name = "Cafe", double strength = .6, params WifiProfileInfo[] profiles) =>
        new(name, ConnectionState.Disconnected, true, strength, WifiSecurityType.Wpa2Psk, profiles);

    private static NetworkDeviceInfo Adapter(params NetworkInfo[] networks) =>
        new(AdapterId, DeviceType.Wifi, "Wi-Fi", "00:11:22:33:44:55", ConnectionState.Disconnected, false, 0, networks, WifiDeviceMode.Station);

    private static NetworkSnapshot Snapshot(params NetworkInfo[] networks) =>
        new(true, [Adapter(networks)], NetworkConnectivity.InternetAccess, true, true,
            [new("profile", "Home", AdapterId, NetworkConnectivity.InternetAccess, NetworkCost.Fixed, false, false, .8)],
            NetworkAccessStatus.Allowed, AirplaneModeState.Off);

    private static NetworkSnapshot RadioOff() => Snapshot() with { WifiEnabled = false, WifiState = WifiRadioState.Off };

    private static WifiProfileInfo Described() => WifiProfiles.Describe(AdapterId, "Office", SavedXml, 0);

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    private static List<string> Failures(Action<Action<string>> subscribe)
    {
        var messages = new List<string>();
        subscribe(messages.Add);
        return messages;
    }

    [Fact]
    public void A_snapshot_changes_only_the_properties_that_differ()
    {
        using var session = new Session(Snapshot(Wifi()));
        var changes = Watch(session.Network);

        session.Backend.Publish(Snapshot(Wifi(strength: .9)));
        Assert.Empty(changes);
        session.Loop.Process();

        Assert.Equal([nameof(WifiNetwork.SignalStrength)], changes);
        Assert.Equal(.9, session.Network.SignalStrength);
    }

    [Fact]
    public void A_snapshot_keeps_the_objects_that_stay_and_lets_go_of_the_others()
    {
        using var session = new Session(Snapshot(Wifi(), Wifi("Other", .4)));
        var device = session.Device;
        var kept = device.Networks[1];
        var profile = Assert.Single(session.Service.Profiles);
        Assert.True(profile.Metered);
        Assert.Equal(AdapterId.ToString(), profile.AdapterId);

        session.Show(Snapshot(Wifi("Other", .9)));

        Assert.Same(device, session.Device);
        Assert.Same(kept, Assert.Single(device.Networks));
        Assert.Same(profile, Assert.Single(session.Service.Profiles));
        Assert.Equal(.9, Assert.IsType<WifiNetwork>(kept).SignalStrength);

        session.Show(NetworkSnapshot.Initial);

        Assert.Empty(session.Service.Devices);
        Assert.Empty(session.Service.Profiles);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_backend()
    {
        var backend = new FakeBackend();
        var loop = new ManualEventLoop();
        var service = new NetworkingService(loop, backend);

        backend.Publish(Snapshot(Wifi()));
        Assert.Empty(service.Devices);
        service.Dispose();
        loop.Process();

        Assert.Empty(service.Devices);
        Assert.True(backend.Unsubscribed);
    }

    [Fact]
    public void Reading_never_performs_an_action_or_asks_for_permission()
    {
        using var session = new Session(Snapshot(Wifi(profiles: Described())));

        _ = session.Profile.Read();

        Assert.Empty(session.Backend.Calls);
    }

    [Fact]
    public async Task Actions_call_the_backend_and_report_failure()
    {
        using var session = new Session(Snapshot(Wifi()));
        var network = session.Network;
        var failed = Failures(h => network.OperationFailed += h);

        await network.ConnectAsync("secret");
        session.Loop.Process();
        Assert.Equal((AdapterId, "Cafe", "secret"), session.Backend.Connection);
        Assert.Equal("", network.Error);
        network.Disconnect();
        session.Loop.Process();
        network.Forget();
        session.Loop.Process();
        await session.Device.ScanAsync();
        session.Loop.Process();
        Assert.Equal(["connect", "disconnect", "forget", "scan"], session.Backend.Calls);

        session.Backend.Failure = new UnauthorizedAccessException("Access revoked");
        await network.ConnectAsync(null);
        session.Loop.Process();
        Assert.False(network.Busy);
        Assert.Equal("Access revoked", network.Error);
        Assert.Equal(["Access revoked"], failed);
        await session.Device.ScanAsync();
        session.Loop.Process();
        Assert.False(session.Device.Scanning);
        Assert.Equal("Access revoked", session.Device.Error);

        session.Backend.Failure = null;
        session.Backend.ConnectResult = ConnectionFailReason.InvalidPassword;
        await network.ConnectAsync("wrong");
        session.Loop.Process();
        Assert.Equal("InvalidPassword", network.Error);
    }

    [Fact]
    public async Task An_action_in_flight_ignores_another_and_a_network_that_left_hears_nothing()
    {
        using var session = new Session(Snapshot(Wifi()));
        var pending = new TaskCompletionSource<ConnectionFailReason?>();
        session.Backend.ConnectTask = pending.Task;
        var network = session.Network;
        var failed = Failures(h => network.OperationFailed += h);

        var first = network.ConnectAsync(null);
        await network.ConnectAsync(null);
        Assert.True(network.Busy);
        Assert.Single(session.Backend.Calls);

        session.Show(Snapshot());
        pending.SetResult(ConnectionFailReason.Timeout);
        await first;
        session.Loop.Process();
        Assert.Equal("", network.Error);
        Assert.Empty(failed);

        network.Connect();
        Assert.Single(session.Backend.Calls);
    }

    [Fact]
    public async Task Access_is_explicit_and_reports_a_failure()
    {
        using var session = new Session(NetworkSnapshot.Initial);
        var completion = new TaskCompletionSource();
        session.Backend.AccessTask = completion.Task;
        var failed = Failures(h => session.Service.OperationFailed += h);

        var request = session.Service.RequestAccessAsync();
        await session.Service.RequestAccessAsync();
        Assert.Single(session.Backend.Calls);
        Assert.True(session.Service.RequestingAccess);
        completion.SetResult();
        await request;
        session.Loop.Process();
        Assert.False(session.Service.RequestingAccess);

        session.Backend.Failure = new UnauthorizedAccessException("Denied");
        await session.Service.RequestAccessAsync();
        session.Loop.Process();
        Assert.Equal("Denied", session.Service.Error);
        Assert.Equal(["Denied"], failed);
    }

    [Theory]
    [InlineData(NetworkCost.Unknown, false, false, false)]
    [InlineData(NetworkCost.Unrestricted, false, false, false)]
    [InlineData(NetworkCost.Fixed, false, false, true)]
    [InlineData(NetworkCost.Variable, false, false, true)]
    [InlineData(NetworkCost.Unrestricted, true, false, true)]
    [InlineData(NetworkCost.Unrestricted, false, true, true)]
    public void Metered_uses_cost_roaming_and_data_limit(NetworkCost cost, bool roaming, bool overLimit, bool expected)
    {
        var profile = new NetworkProfile(new("profile", "Home", AdapterId, NetworkConnectivity.LocalAccess, cost, roaming, overLimit, null));

        Assert.Equal(expected, profile.Metered);
        Assert.False(profile.SignalAvailable);
        Assert.Equal(0, profile.SignalStrength);
    }

    [Fact]
    public void Wired_and_wifi_adapters_report_their_own_state_and_only_a_real_change()
    {
        var wiredId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var wired = new NetworkDeviceInfo(wiredId, DeviceType.Wired, "Ethernet", "aa:bb:cc:dd:ee:ff", ConnectionState.Connected, true, 1000, []);
        NetworkSnapshot Shot(NetworkDeviceInfo wifi, NetworkDeviceInfo ethernet) =>
            new(true, [wifi, ethernet], NetworkConnectivity.InternetAccess, true, true);
        using var session = new Session(Shot(Adapter(), wired));
        var device = Assert.IsType<WifiDevice>(session.Service.Devices[0]);
        var ethernet = Assert.IsType<WiredDevice>(session.Service.Devices[1]);
        Assert.Equal(WifiDeviceMode.Station, device.Mode);
        Assert.Equal(1000, ethernet.LinkSpeed);
        Assert.True(ethernet.HasLink);
        Assert.True(ethernet.Connected);
        var changes = Watch(device);
        var wiredChanges = Watch(ethernet);

        session.Show(Shot(Adapter() with { Name = "Wi-Fi 2" }, wired));
        session.Show(Shot(Adapter() with { Name = "Wi-Fi 2", Mode = WifiDeviceMode.AccessPoint }, wired with { HasLink = false, LinkSpeed = 0 }));

        Assert.Equal([nameof(WifiDevice.Name), nameof(WifiDevice.Mode)], changes);
        Assert.Equal([nameof(WiredDevice.LinkSpeed), nameof(WiredDevice.HasLink)], wiredChanges);
    }

    [Fact]
    public async Task Setting_wifi_follows_the_snapshot_and_a_refusal_stays_until_the_radios_change()
    {
        using var session = new Session(RadioOff());
        var service = session.Service;
        var backend = session.Backend;
        var failed = Failures(h => service.OperationFailed += h);

        await service.SetWifiEnabledAsync(false);
        Assert.Empty(backend.Calls);

        await service.SetWifiEnabledAsync(true);
        Assert.False(service.WifiEnabled);
        session.Loop.Process();
        Assert.True(service.WifiEnabled);
        Assert.Equal("", service.Error);
        Assert.Empty(failed);
        Assert.Equal("wifi-on", Assert.Single(backend.Calls));

        backend.RadioError = "Windows or its policy denied radio control.";
        await service.SetWifiEnabledAsync(false);
        session.Loop.Process();
        Assert.True(service.WifiEnabled);
        Assert.Equal(backend.RadioError, service.Error);
        Assert.Equal([backend.RadioError], failed);

        session.Show(Snapshot());
        Assert.Equal(backend.RadioError, service.Error);

        backend.RadioError = null;
        backend.RadioException = new UnauthorizedAccessException("Access revoked");
        await service.SetWifiEnabledAsync(false);
        session.Loop.Process();
        Assert.Equal("Access revoked", service.Error);

        session.Show(RadioOff());
        Assert.False(service.WifiEnabled);
        Assert.Equal("", service.Error);
    }

    [Fact]
    public async Task A_wifi_request_in_flight_ignores_another_and_a_late_answer_is_dropped()
    {
        using var session = new Session(RadioOff());
        var gate = new TaskCompletionSource<string?>();
        session.Backend.RadioGate = gate;

        var first = session.Service.SetWifiEnabledAsync(true);
        await session.Service.SetWifiEnabledAsync(false);
        Assert.Equal("wifi-on", Assert.Single(session.Backend.Calls));

        session.Service.Dispose();
        gate.SetResult("too late");
        await first;
        session.Loop.Process();
        Assert.Equal("", session.Service.Error);
    }

    [Fact]
    public void Reading_a_profile_returns_its_fields_and_no_key()
    {
        using var session = new Session(Snapshot(Wifi(profiles: Described())));
        var profile = session.Profile;

        var fields = profile.Read();

        Assert.Equal(["id", "name", "ssid", "autoConnect", "security", "hasPassword", "loaded"], fields.Keys);
        Assert.Equal("Wpa2Psk", fields["security"]);
        Assert.Equal(true, fields["hasPassword"]);
        Assert.Equal(WifiProfiles.Id(AdapterId, "Office"), profile.Id);
        Assert.Equal("Office", profile.Name);
        Assert.Equal("Cafe", profile.Ssid);
        Assert.DoesNotContain(Secret, string.Join('|', fields.Values));
        Assert.DoesNotContain(Secret, Described().ToString());
        Assert.DoesNotContain(typeof(WifiProfile).GetProperties(), p => p.Name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_saved_profile_keeps_its_object_and_a_failed_read_raises_nothing()
    {
        var failedRead = new WifiProfileInfo(WifiProfiles.Id(AdapterId, "Office"), "Office", "", false, WifiSecurityType.Unknown, false, false, false, WifiProfileXml.Unreadable);
        using var session = new Session(Snapshot(Wifi(profiles: Described())));
        var profile = session.Profile;
        var failed = Failures(h => profile.OperationFailed += h);

        session.Show(Snapshot(Wifi(profiles: failedRead)));
        Assert.Same(profile, session.Profile);
        Assert.False(profile.Loaded);
        Assert.Equal(WifiProfileXml.Unreadable, profile.Error);
        Assert.Empty(failed);

        var changes = Watch(profile);
        session.Show(Snapshot(Wifi(profiles: failedRead with { AutoConnect = true })));
        Assert.Equal([nameof(WifiProfile.AutoConnect)], changes);

        var second = Described() with { Name = "Office-5", Id = WifiProfiles.Id(AdapterId, "Office-5") };
        session.Show(Snapshot(Wifi(profiles: [Described(), second])));
        Assert.Equal(["Office", "Office-5"], session.Network.SavedProfiles.Select(p => p.Name));
        Assert.Same(profile, session.Network.SavedProfiles[0]);

        session.Show(Snapshot(Wifi()));
        Assert.Empty(session.Network.SavedProfiles);
    }

    [Fact]
    public async Task AutoConnect_waits_for_a_snapshot_and_ignores_an_overlapping_write()
    {
        using var session = new Session(Snapshot(Wifi(profiles: Described())));
        var profile = session.Profile;
        var failed = Failures(h => profile.OperationFailed += h);

        await profile.SetAutoConnectAsync(false);
        session.Loop.Process();
        Assert.True(profile.AutoConnect);
        Assert.Equal("", profile.Error);
        Assert.Equal((AdapterId, "Office", false), Assert.Single(session.Backend.Autos));

        session.Show(Snapshot(Wifi(profiles: Described() with { AutoConnect = false })));
        Assert.Same(profile, session.Profile);
        Assert.False(profile.AutoConnect);

        session.Backend.AutoError = "denied";
        await profile.SetAutoConnectAsync(true);
        session.Loop.Process();
        Assert.Equal(["denied"], failed);
        Assert.Equal("denied", profile.Error);
        Assert.False(profile.AutoConnect);

        session.Backend.AutoError = null;
        session.Backend.AutoException = new UnauthorizedAccessException("Access revoked");
        await profile.SetAutoConnectAsync(true);
        session.Loop.Process();
        Assert.Equal("Access revoked", profile.Error);

        session.Backend.AutoException = null;
        var gate = new TaskCompletionSource<string?>();
        session.Backend.AutoGate = gate;
        session.Backend.Autos.Clear();
        var first = profile.SetAutoConnectAsync(true);
        profile.AutoConnect = true;
        Assert.Equal((AdapterId, "Office", true), Assert.Single(session.Backend.Autos));
        gate.SetResult(null);
        await first;
        session.Loop.Process();
        Assert.False(profile.AutoConnect);
    }

    [Fact]
    public async Task Clear_and_forget_use_the_profile_name()
    {
        using var session = new Session(Snapshot(Wifi(profiles: Described())));
        var profile = session.Profile;
        var failed = Failures(h => profile.OperationFailed += h);

        await profile.ClearPasswordAsync();
        session.Loop.Process();
        Assert.Equal((AdapterId, "Office"), Assert.Single(session.Backend.Clears));

        session.Backend.ForgetError = 5;
        await profile.ForgetAsync();
        session.Loop.Process();
        Assert.Equal((AdapterId, "Office"), Assert.Single(session.Backend.Forgets));
        Assert.Equal([new Win32Exception(5).Message], failed);
        Assert.DoesNotContain(Secret, profile.Error);
    }

    [Fact]
    public async Task Connecting_with_a_profile_uses_the_network_result_and_refuses_another_networks_profile()
    {
        using var session = new Session(Snapshot(Wifi(profiles: Described())));
        var network = session.Network;
        var profile = session.Profile;
        var failed = Failures(h => network.OperationFailed += h);
        var gate = new TaskCompletionSource<string?>();
        session.Backend.ConnectGate = gate;

        var first = network.ConnectProfileAsync(profile);
        Assert.True(network.Busy);
        await network.ConnectProfileAsync(profile);
        Assert.Equal((AdapterId, "Office", true), Assert.Single(session.Backend.Connects));
        gate.SetResult(null);
        await first;
        session.Loop.Process();
        Assert.False(network.Busy);
        Assert.Equal("", network.Error);

        session.Backend.ConnectGate = null;
        session.Backend.ConnectError = "refused";
        await network.ConnectProfileAsync(profile);
        session.Loop.Process();
        Assert.Equal(["refused"], failed);
        Assert.Equal("", profile.Error);

        await network.ConnectProfileAsync(null);
        session.Loop.Process();
        Assert.Equal("That saved profile is not on this network.", network.Error);

        var other = Described() with { Name = "Home", Ssid = "Home", Id = WifiProfiles.Id(AdapterId, "Home") };
        session.Backend.Connects.Clear();
        session.Show(Snapshot(Wifi(profiles: Described()), Wifi("Home", profiles: other)));
        var foreign = Assert.Single(session.Device.Networks[1].SavedProfiles);
        await network.ConnectProfileAsync(foreign);
        session.Loop.Process();
        Assert.Equal("That saved profile is not on this network.", network.Error);

        session.Show(Snapshot(Wifi(profiles: Described())));
        await session.Device.Networks[0].ConnectProfileAsync(foreign);
        session.Loop.Process();
        Assert.Equal("That saved profile is not on this network.", network.Error);
        Assert.Empty(session.Backend.Connects);
    }

    [Fact]
    public async Task A_wired_network_refuses_the_wifi_actions()
    {
        var wiredId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var info = Described() with { Id = WifiProfiles.Id(wiredId, "Office") };
        var wiredNetwork = new NetworkInfo("Cafe", ConnectionState.Connected, true, 0, WifiSecurityType.Unknown, [info]);
        var wired = new NetworkDeviceInfo(wiredId, DeviceType.Wired, "Ethernet", "aa:bb:cc:dd:ee:ff", ConnectionState.Connected, true, 1000, [wiredNetwork]);
        using var session = new Session(new NetworkSnapshot(true, [wired], NetworkConnectivity.LocalAccess, true, true));
        var network = Assert.Single(Assert.IsType<WiredDevice>(Assert.Single(session.Service.Devices)).Networks);
        Assert.IsNotType<WifiNetwork>(network);

        await network.ConnectProfileAsync(Assert.Single(network.SavedProfiles));
        session.Loop.Process();
        Assert.Equal("Windows manages wired connections automatically.", network.Error);

        network.Forget();
        session.Loop.Process();
        Assert.Equal("Only saved Wi-Fi profiles can be forgotten.", network.Error);
        Assert.Empty(session.Backend.Calls);
    }

    private sealed class Session : IDisposable
    {
        public Session(NetworkSnapshot snapshot)
        {
            Service = new NetworkingService(Loop, Backend);
            Show(snapshot);
        }

        public FakeBackend Backend { get; } = new();

        public ManualEventLoop Loop { get; } = new();

        public NetworkingService Service { get; }

        public WifiDevice Device => Assert.IsType<WifiDevice>(Assert.Single(Service.Devices));

        public WifiNetwork Network => Assert.IsType<WifiNetwork>(Device.Networks[0]);

        public WifiProfile Profile => Assert.Single(Network.SavedProfiles);

        public void Show(NetworkSnapshot snapshot)
        {
            Backend.Publish(snapshot);
            Loop.Process();
        }

        public void Dispose() => Service.Dispose();
    }

    private sealed class FakeBackend : INetworkBackend
    {
        private Action<NetworkSnapshot>? _listener;

        public NetworkSnapshot Current { get; private set; } = NetworkSnapshot.Initial;

        public List<string> Calls { get; } = [];

        public (Guid, string, string?) Connection { get; private set; }

        public List<(Guid Device, string Name, bool Auto)> Autos { get; } = [];

        public List<(Guid Device, string Name)> Clears { get; } = [];

        public List<(Guid Device, string Name)> Forgets { get; } = [];

        public List<(Guid Device, string Name, bool Hidden)> Connects { get; } = [];

        public Exception? Failure { get; set; }

        public ConnectionFailReason? ConnectResult { get; set; }

        public Task<ConnectionFailReason?>? ConnectTask { get; set; }

        public Task AccessTask { get; set; } = Task.CompletedTask;

        public string? RadioError { get; set; }

        public Exception? RadioException { get; set; }

        public TaskCompletionSource<string?>? RadioGate { get; set; }

        public string? AutoError { get; set; }

        public Exception? AutoException { get; set; }

        public TaskCompletionSource<string?>? AutoGate { get; set; }

        public string? ConnectError { get; set; }

        public TaskCompletionSource<string?>? ConnectGate { get; set; }

        public uint ForgetError { get; set; }

        public bool Unsubscribed { get; private set; }

        public IDisposable Subscribe(Action<NetworkSnapshot> listener)
        {
            _listener = listener;
            return new Subscription(this);
        }

        public void Publish(NetworkSnapshot snapshot)
        {
            Current = snapshot;
            _listener?.Invoke(snapshot);
        }

        public void Refresh() => Calls.Add("refresh");

        public Task RequestAccessAsync()
        {
            Calls.Add("access");
            return Failure is { } e ? Task.FromException(e) : AccessTask;
        }

        public Task<string?> SetWifiEnabledAsync(bool enabled)
        {
            Calls.Add(enabled ? "wifi-on" : "wifi-off");
            if (RadioGate is { } gate)
                return FinishRadio(gate.Task, enabled);
            if (RadioException is { } exception)
                return Task.FromException<string?>(exception);
            return Task.FromResult(ApplyRadio(enabled, RadioError));
        }

        private async Task<string?> FinishRadio(Task<string?> pending, bool enabled) => ApplyRadio(enabled, await pending.ConfigureAwait(false));

        private string? ApplyRadio(bool enabled, string? error)
        {
            if (error is null)
                Current = Current with { WifiEnabled = enabled, WifiHardwareEnabled = true, WifiState = enabled ? WifiRadioState.On : WifiRadioState.Off };
            return error;
        }

        public Task ScanAsync(Guid device)
        {
            Calls.Add("scan");
            Assert.Equal(AdapterId, device);
            return Failure is { } e ? Task.FromException(e) : Task.CompletedTask;
        }

        public Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? password)
        {
            Calls.Add("connect");
            Connection = (device, ssid, password);
            return Failure is { } e ? Task.FromException<ConnectionFailReason?>(e) : ConnectTask ?? Task.FromResult(ConnectResult);
        }

        public bool Disconnect(Guid device)
        {
            Calls.Add("disconnect");
            Assert.Equal(AdapterId, device);
            return true;
        }

        public uint Forget(Guid device, string profileName)
        {
            Calls.Add("forget");
            Forgets.Add((device, profileName));
            return ForgetError;
        }

        public Task<string?> SetProfileAutoConnectAsync(Guid device, string profileName, bool autoConnect)
        {
            Calls.Add("profile-auto");
            Autos.Add((device, profileName, autoConnect));
            if (AutoException is { } exception)
                throw exception;
            return AutoGate?.Task ?? Task.FromResult(AutoError);
        }

        public Task<string?> ClearProfilePasswordAsync(Guid device, string profileName)
        {
            Calls.Add("profile-clear");
            Clears.Add((device, profileName));
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ConnectProfileAsync(Guid device, string profileName, bool hidden)
        {
            Calls.Add("profile-connect");
            Connects.Add((device, profileName, hidden));
            return ConnectGate?.Task ?? Task.FromResult(ConnectError);
        }

        private sealed class Subscription(FakeBackend owner) : IDisposable
        {
            public void Dispose()
            {
                owner.Unsubscribed = true;
                owner._listener = null;
            }
        }
    }
}
