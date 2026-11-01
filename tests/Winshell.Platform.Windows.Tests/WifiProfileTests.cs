using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Winshell.Platform.Windows.Services.Network;
using Winshell.Runtime;
using Winshell.Runtime.Shell;

namespace Winshell.Platform.Windows.Tests;

public sealed class WifiProfileTests
{
    private const string Secret = "winshell-profile-key-9f3a";
    private const string Marker = "winshell-profile-marker";

    private const string Saved = """
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
        	<!-- <connectionMode>auto</connectionMode> -->
        	<name>Office</name>
        	<SSIDConfig>
        		<SSID>
        			<hex>43616665</hex>
        			<name>Cafe</name>
        		</SSID>
        		<nonBroadcast>true</nonBroadcast>
        	</SSIDConfig>
        	<connectionMode note="keep">auto</connectionMode>
        	<connectionType>ESS</connectionType>
        	<MSM>
        		<security>
        			<authEncryption>
        				<authentication>WPA2PSK</authentication>
        				<encryption>AES</encryption>
        				<useOneX>false</useOneX>
        			</authEncryption>
        			<sharedKey>
        				<keyType>passPhrase</keyType>
        				<protected>true</protected>
        				<keyMaterial>winshell-profile-key-9f3a</keyMaterial>
        			</sharedKey>
        		</security>
        	</MSM>
        	<MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
        		<enableRandomization>true</enableRandomization>
        		<randomizationSeed>keep-me</randomizationSeed>
        	</MacRandomization>
        </WLANProfile>
        """;

    private static readonly string Manual = Saved.Replace(
        "<connectionMode note=\"keep\">auto</connectionMode>",
        "<connectionMode note=\"keep\">manual</connectionMode>",
        StringComparison.Ordinal);

    private static readonly Guid AdapterId = Guid.Parse("0b0f2811-87b5-48d6-9d7a-7a26ae12ff33");

    [Fact]
    public void Round_trip_changes_only_the_connection_mode()
    {
        Assert.NotEqual(Saved, Manual);
        Assert.True(WifiProfileXml.TryParse(Saved, out var fields, out var error), error);
        Assert.Equal("", error);
        Assert.Equal("Office", fields.Name);
        Assert.Equal("Cafe", fields.Ssid);
        Assert.Equal("auto", fields.ConnectionMode);
        Assert.True(fields.AutoConnect);
        Assert.True(fields.HasPassword);
        Assert.True(fields.Hidden);
        Assert.Equal(WifiSecurityType.Wpa2Psk, fields.Security);
        Assert.DoesNotContain(Secret, fields.ToString());

        Assert.True(WifiProfileXml.TrySetAutoConnect(Saved, true, out var same, out var changed, out error), error);
        Assert.False(changed);
        Assert.Equal(Saved, same);

        Assert.True(WifiProfileXml.TrySetAutoConnect(Saved, false, out var updated, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("", error);
        Assert.Equal(Manual, updated);
        Assert.Contains("<!-- <connectionMode>auto</connectionMode> -->", updated);
        Assert.Contains(Secret, updated);
        Assert.Contains("randomizationSeed", updated);
        Assert.True(WifiProfileXml.TryParse(updated, out var after, out error), error);
        Assert.False(after.AutoConnect);
        Assert.Equal("manual", after.ConnectionMode);
        Assert.True(after.HasPassword);
        Assert.Equal(fields.Ssid, after.Ssid);
        Assert.Equal(fields.Security, after.Security);
        Assert.Equal(fields.Hidden, after.Hidden);
        Assert.DoesNotContain(Secret, after.ToString());
        Assert.DoesNotContain(Secret, error);
    }

    [Fact]
    public void Clear_password_removes_only_the_key_element()
    {
        var cleared = Saved.Replace("<keyMaterial>" + Secret + "</keyMaterial>", "", StringComparison.Ordinal);
        Assert.NotEqual(Saved, cleared);
        Assert.True(WifiProfileXml.TryClearPassword(Saved, out var updated, out var changed, out var error), error);
        Assert.True(changed);
        Assert.Equal("", error);
        Assert.Equal(cleared, updated);
        Assert.DoesNotContain(Secret, updated);
        Assert.Contains("<protected>true</protected>", updated);
        Assert.Contains("MacRandomization", updated);
        Assert.True(WifiProfileXml.TryParse(updated, out var after, out error), error);
        Assert.False(after.HasPassword);
        Assert.Equal("auto", after.ConnectionMode);
        Assert.Equal("Cafe", after.Ssid);
        Assert.DoesNotContain(Secret, after.ToString());

        const string prefixed = """
            <WLANProfile><name>Office</name><connectionMode>auto</connectionMode><sharedKey><keyMaterial /><p:keyMaterial xmlns:p="urn:p">winshell-profile-key-9f3a</p:keyMaterial><protected>true</protected></sharedKey><Vendor xmlns="urn:vendor">keep</Vendor></WLANProfile>
            """;
        Assert.True(WifiProfileXml.TryParse(prefixed, out var before, out error), error);
        Assert.True(before.HasPassword);
        Assert.DoesNotContain(Secret, before.ToString());
        Assert.True(WifiProfileXml.TryClearPassword(prefixed, out updated, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal(
            """
            <WLANProfile><name>Office</name><connectionMode>auto</connectionMode><sharedKey><protected>true</protected></sharedKey><Vendor xmlns="urn:vendor">keep</Vendor></WLANProfile>
            """,
            updated);
        Assert.DoesNotContain(Secret, updated);

        const string comment = "<WLANProfile><name>Office</name><!-- <keyMaterial>winshell-profile-key-9f3a</keyMaterial> --><connectionMode>auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(comment, out var commented, out error), error);
        Assert.False(commented.HasPassword);
        Assert.DoesNotContain(Secret, commented.ToString());
        Assert.True(WifiProfileXml.TryClearPassword(comment, out updated, out changed, out error), error);
        Assert.False(changed);
        Assert.Equal(comment, updated);

        var nested = "<WLANProfile><name>Office<nested>" + Secret + "</nested></name><connectionMode>auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(nested, out var name, out error), error);
        Assert.Equal("Office", name.Name);
        Assert.False(name.HasPassword);
        Assert.DoesNotContain(Secret, name.ToString());
    }

    [Fact]
    public void Hostile_profiles_are_refused_without_a_write()
    {
        AssertNoWrite(new string('a', WifiProfileXml.MaxCharacters + 1), WifiProfileXml.TooLarge);
        AssertNoWrite("<WLANProfile><keyMaterial>" + Secret, WifiProfileXml.Unreadable);
        AssertNoWrite(Deep(40), WifiProfileXml.Unreadable);
        Assert.True(WifiProfileXml.TryParse(Deep(8), out _, out var shallow), shallow);
        Assert.Equal("", shallow);

        var dtd = """
            <?xml version="1.0"?>
            <!DOCTYPE WLANProfile [
            <!ENTITY secret SYSTEM "file:///C:/winshell-profile-marker">
            ]>
            <WLANProfile><name>&secret;</name></WLANProfile>
            """;
        AssertNoWrite(dtd, WifiProfileXml.Unreadable);
        Assert.DoesNotContain(Marker, WifiProfileXml.Unreadable);

        var billion = """
            <!DOCTYPE lolz [
            <!ENTITY lol "lol">
            <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
            <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
            <!ENTITY lol4 "&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;">
            <!ENTITY lol5 "&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;">
            <!ENTITY lol6 "&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;">
            <!ENTITY lol7 "&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;">
            <!ENTITY lol8 "&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;">
            <!ENTITY lol9 "&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;">
            ]>
            <WLANProfile><name>&lol9;</name></WLANProfile>
            """;
        AssertNoWrite(billion, WifiProfileXml.Unreadable);

        var api = new FakeWlan { Xml = dtd };
        var clear = WifiProfileEdit.ClearPassword(api, AdapterId, "Office");
        Assert.Equal(0, api.Sets);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);
        Assert.False(clear.Wrote);
        Assert.DoesNotContain(Marker, clear.Error);
        Assert.DoesNotContain(Secret, clear.Error);
    }

    [Theory]
    [InlineData("open", "none", WifiSecurityType.Open)]
    [InlineData("open", "WEP", WifiSecurityType.StaticWep)]
    [InlineData("shared", "none", WifiSecurityType.StaticWep)]
    [InlineData("WPA", "TKIP", WifiSecurityType.WpaEap)]
    [InlineData("WPAPSK", "TKIP", WifiSecurityType.WpaPsk)]
    [InlineData("WPA2", "AES", WifiSecurityType.Wpa2Eap)]
    [InlineData("WPA3ENT", "AES", WifiSecurityType.Wpa2Eap)]
    [InlineData("WPA2PSK", "AES", WifiSecurityType.Wpa2Psk)]
    [InlineData("wpa2psk", "aes", WifiSecurityType.Wpa2Psk)]
    [InlineData("WPA3ENT192", "GCMP256", WifiSecurityType.Wpa3SuiteB192)]
    [InlineData("WPA3SAE", "AES", WifiSecurityType.Sae)]
    [InlineData("WPA3", "AES", WifiSecurityType.Sae)]
    [InlineData("OWE", "AES", WifiSecurityType.Owe)]
    [InlineData("other", "AES", WifiSecurityType.Unknown)]
    public void Authentication_maps_like_the_scan(string authentication, string encryption, WifiSecurityType expected)
    {
        var xml = "<WLANProfile><name>Office</name><SSIDConfig><SSID><name>Cafe</name></SSID></SSIDConfig><connectionMode>manual</connectionMode><MSM><security><authEncryption><authentication>"
            + authentication + "</authentication><encryption>" + encryption + "</encryption></authEncryption></security></MSM></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(xml, out var fields, out var error), error);
        Assert.Equal(expected, fields.Security);
        Assert.False(fields.AutoConnect);
        Assert.False(fields.HasPassword);
        Assert.DoesNotContain(Secret, fields.ToString());
    }

    [Fact]
    public void Ssid_prefers_the_name_and_rejects_a_bad_hex_value()
    {
        Assert.Equal("Cafe", Ssid("<hex>43616665</hex><name>Cafe</name>"));
        Assert.Equal("Cafe", Ssid("<hex>43616665</hex>"));
        Assert.Equal("Caf\u00e9", Ssid("<hex>436166C3A9</hex>"));
        Assert.Equal("", Ssid("<hex>zzzz</hex>"));
        Assert.Equal("", Ssid("<hex>abc</hex>"));
    }

    [Fact]
    public void A_mode_write_fails_closed_when_the_element_is_not_one_plain_value()
    {
        const string spaced = "<WLANProfile><name>Office</name><connectionMode> auto </connectionMode><Vendor>keep</Vendor></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(spaced, true, out var same, out var changed, out var error), error);
        Assert.False(changed);
        Assert.Equal(spaced, same);
        Assert.True(WifiProfileXml.TrySetAutoConnect(spaced, false, out var manual, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>manual</connectionMode><Vendor>keep</Vendor></WLANProfile>", manual);

        const string capital = "<WLANProfile><name>Office</name><connectionMode>Auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(capital, true, out same, out changed, out error), error);
        Assert.False(changed);
        Assert.Equal(capital, same);

        const string foo = "<WLANProfile><name>Office</name><connectionMode>foo</connectionMode><Vendor>keep</Vendor></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(foo, false, out manual, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>manual</connectionMode><Vendor>keep</Vendor></WLANProfile>", manual);

        const string nested = "<WLANProfile><name>Office</name><connectionMode>auto</connectionMode><Vendor><connectionMode>auto</connectionMode><keep>1</keep></Vendor></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(nested, false, out manual, out changed, out error), error);
        Assert.True(changed);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>manual</connectionMode><Vendor><connectionMode>auto</connectionMode><keep>1</keep></Vendor></WLANProfile>", manual);

        const string empty = "<WLANProfile><name>Office</name><connectionMode></connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TrySetAutoConnect(empty, true, out manual, out changed, out error), error);
        Assert.Equal("<WLANProfile><name>Office</name><connectionMode>auto</connectionMode></WLANProfile>", manual);

        AssertNoWrite("<WLANProfile><name>Office</name><connectionMode>auto</connectionMode><connectionMode>auto</connectionMode></WLANProfile>", WifiProfileXml.Rejected, auto: false);
        AssertNoWrite("<WLANProfile><name>Office</name></WLANProfile>", WifiProfileXml.Rejected, auto: true);
        AssertNoWrite("<WLANProfile><name>Office</name><connectionMode/></WLANProfile>", WifiProfileXml.Rejected, auto: true);
        AssertNoWrite("<WLANProfile><name>Office</name><connectionMode><![CDATA[auto]]></connectionMode></WLANProfile>", WifiProfileXml.Rejected, auto: false);
    }

    [Theory]
    [InlineData(0x6u, 0x2u)]
    [InlineData(0x4u, 0x0u)]
    [InlineData(0x1u, 0x1u)]
    [InlineData(0x3u, 0x3u)]
    [InlineData(0x0u, 0x0u)]
    public void Writes_keep_the_profile_flag_and_drop_a_plaintext_bit(uint reported, uint written)
    {
        var api = new FakeWlan { Xml = Saved, Flags = reported };
        var change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", false);
        Assert.Null(change.Error);
        Assert.True(change.Wrote);
        Assert.Equal(written, api.WrittenFlags);
        Assert.Equal(Manual, api.WrittenXml);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);
        Assert.DoesNotContain(Secret, change.Error ?? "");
    }

    [Fact]
    public void A_refused_all_users_write_reports_the_windows_error()
    {
        var denied = new Win32Exception(5).Message;
        var api = new FakeWlan { Xml = Saved, Flags = 0, SetError = 5 };
        var change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", false);
        Assert.True(change.Wrote);
        Assert.Equal(denied, change.Error);
        Assert.Equal(0u, api.WrittenFlags);
        Assert.Contains(Secret, api.WrittenXml);
        Assert.DoesNotContain(Secret, change.Error);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);

        api = new FakeWlan { Xml = Saved, GetError = 5 };
        change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", false);
        Assert.False(change.Wrote);
        Assert.Equal(0, api.Sets);
        Assert.Equal(denied, change.Error);
        Assert.DoesNotContain(Secret, change.Error);

        api = new FakeWlan { Xml = Saved };
        change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", true);
        Assert.False(change.Wrote);
        Assert.Equal(0, api.Sets);
        Assert.Null(change.Error);

        const string noKey = "<WLANProfile><name>Office</name><connectionMode>auto</connectionMode></WLANProfile>";
        api = new FakeWlan { Xml = noKey };
        change = WifiProfileEdit.ClearPassword(api, AdapterId, "Office");
        Assert.False(change.Wrote);
        Assert.Equal(0, api.Sets);
        Assert.Null(change.Error);
    }

    [Fact]
    public void Profile_reads_request_no_plaintext_key_and_the_connect_struct_matches_native_layout()
    {
        Assert.Equal(0u, NetworkNative.ProfileReadFlags());
        Assert.Equal(0u, NetworkNative.ProfileReadFlags() & 0x4);
        Assert.Equal(0u, NetworkNative.ProfileWriteFlags(0x4));
        Assert.Equal(0x2u, NetworkNative.ProfileWriteFlags(0x6));
        Assert.Equal((nint)0, Marshal.OffsetOf<NetworkNative.WlanConnectionParameters>("Mode"));
        Assert.Equal((nint)IntPtr.Size, Marshal.OffsetOf<NetworkNative.WlanConnectionParameters>("Profile"));
        Assert.Equal((nint)(IntPtr.Size * 2), Marshal.OffsetOf<NetworkNative.WlanConnectionParameters>("Ssid"));
        Assert.Equal((nint)(IntPtr.Size * 3), Marshal.OffsetOf<NetworkNative.WlanConnectionParameters>("BssidList"));
        Assert.Equal((nint)(IntPtr.Size * 4), Marshal.OffsetOf<NetworkNative.WlanConnectionParameters>("BssType"));
        Assert.Equal((nint)(IntPtr.Size * 4 + 4), Marshal.OffsetOf<NetworkNative.WlanConnectionParameters>("Flags"));
    }

    [Fact]
    public void A_loaded_profile_matches_its_ssid_and_a_failed_read_matches_its_name()
    {
        var loaded = WifiProfiles.Describe(AdapterId, "Office", Saved, 0);
        Assert.Equal(AdapterId + ":Office", loaded.Id);
        Assert.True(loaded.Loaded);
        Assert.Equal("Cafe", loaded.Ssid);
        Assert.True(loaded.HasPassword);
        Assert.True(loaded.Hidden);
        Assert.Equal("", loaded.Error);
        Assert.True(WifiProfiles.Matches(loaded, "Cafe"));
        Assert.False(WifiProfiles.Matches(loaded, "Office"));
        Assert.DoesNotContain(Secret, loaded.ToString());

        var failed = WifiProfiles.Describe(AdapterId, "Cafe", Saved, 5);
        Assert.False(failed.Loaded);
        Assert.Equal("", failed.Ssid);
        Assert.False(failed.HasPassword);
        Assert.Equal(new Win32Exception(5).Message, failed.Error);
        Assert.DoesNotContain(Secret, failed.Error);
        Assert.DoesNotContain("<", failed.Error);
        Assert.True(WifiProfiles.Matches(failed, "Cafe"));
        Assert.False(WifiProfiles.Matches(failed, "Office"));

        var unread = WifiProfiles.Describe(AdapterId, "Cafe", null, 0);
        Assert.Equal(WifiProfileXml.Unreadable, unread.Error);
        Assert.True(WifiProfiles.Matches(unread, "Cafe"));
    }

    [Fact]
    public async Task Profile_writes_run_one_at_a_time()
    {
        var queue = new ProfileWriteQueue();
        using var hold = new ManualResetEventSlim();
        var firstInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var max = 0;

        var first = queue.RunAsync(() => Enter(firstInside));
        await firstInside.Task;
        var second = queue.RunAsync(() => Enter(secondInside));
        Assert.Equal(1, Volatile.Read(ref inFlight));
        hold.Set();
        Assert.Equal(1, await first);
        await secondInside.Task;
        Assert.Equal(1, await second);
        Assert.Equal(1, max);

        int Enter(TaskCompletionSource inside)
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            do
            {
                seen = Volatile.Read(ref max);
            }
            while (now > seen && Interlocked.CompareExchange(ref max, now, seen) != seen);
            inside.SetResult();
            hold.Wait(TestContext.Current.CancellationToken);
            Interlocked.Decrement(ref inFlight);
            return now;
        }
    }

    [Fact]
    public void WifiProfile_is_not_creatable_and_has_no_settings_signal()
    {
        var registry = new ModuleRegistry();
        NetworkingModule.Register(registry);
        Assert.True(registry.TryGetModule("Winshell.Network", out var module));
        Assert.False(module.Types["WifiProfile"].Creatable);

        using var session = Session.Open(Described());
        Assert.Empty(session.Backend.Calls);
        Assert.True(session.Network.HasMember("savedProfiles"));
        Assert.True(session.Network.HasMember("connectWithProfile"));
        Assert.False(session.Network.HasMember("nmSettings"));
        Assert.False(session.Network.HasMember("connectWithSettings"));
        Assert.False(session.Network.HasMember("settingsChanged"));
        Assert.Null(session.Profile.FindSignal("settingsChanged"));
        Assert.False(session.Profile.HasMember("clearSecrets"));
        Assert.True(session.Profile.HasMember("clearPassword"));
        Assert.True(session.Profile.HasMember("autoConnect"));
        Assert.Equal(AdapterId + ":Office", session.Profile.Id);
        Assert.Equal("Office", session.Profile.Name);
        Assert.Equal("Cafe", session.Profile.Ssid);
        var fields = session.Profile.Read();
        Assert.Equal(["id", "name", "ssid", "autoConnect", "security", "hasPassword", "loaded"], fields.Keys);
        Assert.Equal("Wpa2Psk", fields["security"]);
        Assert.Equal(true, fields["hasPassword"]);
        Assert.Equal(true, fields["loaded"]);
        Assert.False(fields.ContainsKey("password"));
        Assert.False(fields.ContainsKey("keyMaterial"));
        Assert.False(fields.ContainsKey("key"));
        Assert.DoesNotContain(Secret, string.Join("|", fields.Values));
    }

    [Fact]
    public void Saved_profiles_keep_identity_and_a_failed_read_does_not_signal()
    {
        using var session = Session.Open(Net("Cafe"));
        var signals = 0;
        session.Network.SavedProfiles.Connect("objectInsertedPost", args =>
            ((WifiProfile)args[0]!).Connect("operationFailed", _ => signals++));
        var failed = new WifiProfileInfo(WifiProfiles.Id(AdapterId, "Cafe"), "Cafe", "", false, WifiSecurityType.Unknown, false, false, false, WifiProfileXml.Unreadable);
        session.Service.Apply(Shot(Net("Cafe", failed)));
        Assert.Equal(0, signals);
        Assert.False(session.Profile.Loaded);
        Assert.Equal(WifiProfileXml.Unreadable, session.Profile.Error);
        Assert.Empty(session.Backend.Calls);

        var described = Described();
        session.Service.Apply(Shot(Net("Cafe", described)));
        var profile = session.Profile;
        var auto = 0;
        var password = 0;
        profile.Connect("autoConnectChanged", _ => auto++);
        profile.Connect("hasPasswordChanged", _ => password++);
        session.Service.Apply(Shot(Net("Cafe", described with { AutoConnect = false })));
        Assert.Same(profile, session.Profile);
        Assert.Equal(1, auto);
        Assert.Equal(0, password);
        Assert.False(profile.AutoConnect);
        session.Service.Apply(Shot(Net("Cafe", described with { AutoConnect = false, HasPassword = false })));
        Assert.Same(profile, session.Profile);
        Assert.Equal(1, password);
        Assert.False(profile.HasPassword);

        var second = Described() with { Name = "Office-5", Id = WifiProfiles.Id(AdapterId, "Office-5") };
        session.Service.Apply(Shot(Net("Cafe", described with { AutoConnect = false, HasPassword = false }, second)));
        Assert.Equal(["Office", "Office-5"], session.Network.SavedProfiles.Rows.Cast<WifiProfile>().Select(p => p.Name));
        session.Service.Apply(Shot(Net("Cafe")));
        Assert.True(profile.IsDestroyed);
        Assert.Empty(session.Network.SavedProfiles.Rows);
    }

    [Fact]
    public async Task AutoConnect_waits_for_a_snapshot_and_ignores_an_overlapping_write()
    {
        using var session = Session.Open(Described());
        var profile = session.Profile;
        await profile.SetAutoConnectAsync(false);
        session.Loop.Process();
        Assert.True(profile.AutoConnect);
        Assert.Equal("", profile.Error);
        Assert.Equal((AdapterId, "Office", false), Assert.Single(session.Backend.Autos));
        session.Service.Apply(Shot(Net("Cafe", Described() with { AutoConnect = false })));
        Assert.Same(profile, session.Profile);
        Assert.False(profile.AutoConnect);

        session.Backend.AutoError = "denied";
        string? message = null;
        profile.Connect("operationFailed", args => message = (string)args[0]!);
        await profile.SetAutoConnectAsync(true);
        session.Loop.Process();
        Assert.Equal("denied", message);
        Assert.Equal("denied", profile.Error);
        Assert.False(profile.AutoConnect);

        session.Backend.AutoError = null;
        session.Backend.AutoException = new UnauthorizedAccessException("Access revoked");
        await profile.SetAutoConnectAsync(true);
        session.Loop.Process();
        Assert.Equal("Access revoked", profile.Error);
        Assert.False(profile.AutoConnect);

        session.Backend.AutoException = null;
        profile.Update(Described());
        var gate = new TaskCompletionSource<string?>();
        session.Backend.AutoGate = gate;
        session.Backend.Autos.Clear();
        var first = profile.SetAutoConnectAsync(false);
        profile.AutoConnect = false;
        Assert.Equal((AdapterId, "Office", false), Assert.Single(session.Backend.Autos));
        gate.SetResult(null);
        await first;
        session.Loop.Process();
        Assert.True(profile.AutoConnect);
    }

    [Fact]
    public async Task Clear_and_forget_use_the_profile_name()
    {
        using var session = Session.Open(Described());
        await session.Profile.ClearPasswordAsync();
        session.Loop.Process();
        Assert.Equal((AdapterId, "Office"), Assert.Single(session.Backend.Clears));

        string? message = null;
        session.Profile.Connect("operationFailed", args => message = (string)args[0]!);
        session.Backend.ForgetError = 5;
        await session.Profile.ForgetAsync();
        session.Loop.Process();
        Assert.Equal((AdapterId, "Office"), Assert.Single(session.Backend.Forgets));
        Assert.Equal(new Win32Exception(5).Message, message);
        Assert.DoesNotContain(Secret, message);
        Assert.False(session.Profile.IsDestroyed);
    }

    [Fact]
    public async Task Connect_with_profile_uses_the_network_result_and_rejects_the_wrong_profile()
    {
        using var session = Session.Open(Described() with { Hidden = true });
        var gate = new TaskCompletionSource<string?>();
        session.Backend.ConnectGate = gate;
        var first = session.Network.ConnectProfileAsync(session.Profile);
        Assert.True(session.Network.Busy);
        await session.Network.ConnectProfileAsync(session.Profile);
        Assert.Equal((AdapterId, "Office", true), Assert.Single(session.Backend.Connects));
        gate.SetResult(null);
        await first;
        session.Loop.Process();
        Assert.False(session.Network.Busy);
        Assert.Equal("", session.Network.Error);

        session.Backend.ConnectGate = null;
        session.Backend.ConnectError = "refused";
        string? message = null;
        session.Network.Connect("operationFailed", args => message = (string)args[0]!);
        await session.Network.ConnectProfileAsync(session.Profile);
        session.Loop.Process();
        Assert.Equal("refused", message);
        Assert.Equal("refused", session.Network.Error);
        Assert.Equal("", session.Profile.Error);

        await session.Network.ConnectProfileAsync(null);
        session.Loop.Process();
        Assert.Equal("That saved profile is not on this network.", session.Network.Error);

        var foreign = Session.Open(Net("Home", Described() with { Name = "Home", Ssid = "Home", Id = WifiProfiles.Id(AdapterId, "Home") }), Net("Cafe", Described()));
        using (foreign)
        {
            var device = Assert.IsType<WifiDevice>(Assert.Single(foreign.Service.Devices.Rows));
            var home = Assert.IsType<WifiNetwork>(device.Networks.Rows[0]);
            var cafe = Assert.IsType<WifiProfile>(Assert.Single(Assert.IsType<WifiNetwork>(device.Networks.Rows[1]).SavedProfiles.Rows));
            await home.ConnectProfileAsync(cafe);
            foreign.Loop.Process();
            Assert.Equal("That saved profile is not on this network.", home.Error);
            Assert.Empty(foreign.Backend.Connects);
        }

        var wiredId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var info = Described() with { Id = WifiProfiles.Id(wiredId, "Office") };
        var wiredNetwork = new NetworkInfo("Cafe", ConnectionState.Connected, true, 0, WifiSecurityType.Unknown, [info]);
        var wiredDevice = new NetworkDeviceInfo(wiredId, DeviceType.Wired, "Ethernet", "aa:bb:cc:dd:ee:ff", ConnectionState.Connected, true, 1000, [wiredNetwork]);
        using var wired = Session.Open(new NetworkSnapshot(true, [wiredDevice], NetworkConnectivity.LocalAccess, true, true));
        var network = Assert.IsType<QmlNetwork>(Assert.Single(Assert.IsType<WiredDevice>(Assert.Single(wired.Service.Devices.Rows)).Networks.Rows));
        await network.ConnectProfileAsync(Assert.IsType<WifiProfile>(Assert.Single(network.SavedProfiles.Rows)));
        wired.Loop.Process();
        Assert.Equal("Windows manages wired connections automatically.", network.Error);
        Assert.Empty(wired.Backend.Connects);
    }

    [Fact]
    public void Qml_reads_the_fields_and_does_not_see_the_key()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-network-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var session = Session.Open(Described());
        try
        {
            File.WriteAllText(Path.Combine(directory, "shell.qml"), """
                import QtQml
                import Winshell.Network
                QtObject {
                    property string security: ""
                    property bool hasPassword: false
                    property string dump: ""
                    Component.onCompleted: {
                        var fields = TestNetwork.devices.values[0].networks.values[0].savedProfiles.values[0].read()
                        security = fields.security
                        hasPassword = fields.hasPassword
                        dump = JSON.stringify(fields)
                    }
                }
                """);
            var registry = StandardModules.Create();
            NetworkingModule.Register(registry);
            registry.Module("Winshell.Network").Singleton(_ => session.Service, "TestNetwork");
            using var engine = new QmlEngine(registry, session.Loop, new Log(), directory);
            session.Service.Engine = engine;
            session.Service.Apply(Shot(Net("Cafe", Described())));
            var root = engine.CreateRoot(Path.Combine(directory, "shell.qml"));
            Assert.Equal("Wpa2Psk", root.ReadProperty("security"));
            Assert.True((bool)root.ReadProperty("hasPassword")!);
            var dump = (string)root.ReadProperty("dump")!;
            Assert.Contains("\"security\":\"Wpa2Psk\"", dump);
            Assert.DoesNotContain(Secret, dump);
            Assert.DoesNotContain("keyMaterial", dump);
            Assert.Empty(session.Backend.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertNoWrite(string xml, string expected, bool auto = false)
    {
        Assert.False(WifiProfileXml.TrySetAutoConnect(xml, auto, out var updated, out var changed, out var error));
        Assert.False(changed);
        Assert.Equal(expected, error);
        Assert.Equal(xml, updated);
        Assert.DoesNotContain(Secret, error);
        Assert.DoesNotContain(Marker, error);
        var api = new FakeWlan { Xml = xml };
        var change = WifiProfileEdit.SetAutoConnect(api, AdapterId, "Office", auto);
        Assert.Equal(0, api.Sets);
        Assert.Equal(0, api.Connects);
        Assert.Equal(0, api.Deletes);
        Assert.False(change.Wrote);
        Assert.Equal(expected, change.Error);
        Assert.DoesNotContain(Secret, change.Error);
        Assert.DoesNotContain(Marker, change.Error);
    }

    private static string Deep(int levels)
    {
        var builder = new StringBuilder("<WLANProfile><name>Office</name>");
        for (var i = 0; i < levels; i++)
            builder.Append("<e>");
        builder.Append("x");
        for (var i = 0; i < levels; i++)
            builder.Append("</e>");
        builder.Append("</WLANProfile>");
        return builder.ToString();
    }

    private static string Ssid(string inner)
    {
        var xml = "<WLANProfile><name>Office</name><SSIDConfig><SSID>" + inner + "</SSID></SSIDConfig><connectionMode>auto</connectionMode></WLANProfile>";
        Assert.True(WifiProfileXml.TryParse(xml, out var fields, out var error), error);
        Assert.True(fields.Name.Length > 0);
        return fields.Ssid;
    }

    private static WifiProfileInfo Described() => WifiProfiles.Describe(AdapterId, "Office", Saved, 0);

    private static NetworkInfo Net(string ssid, params WifiProfileInfo[] profiles) =>
        new(ssid, ConnectionState.Disconnected, profiles.Length > 0, .5, WifiSecurityType.Wpa2Psk, profiles);

    private static NetworkSnapshot Shot(params NetworkInfo[] networks) =>
        new(true, [new NetworkDeviceInfo(AdapterId, DeviceType.Wifi, "Wi-Fi", "00:11:22:33:44:55", ConnectionState.Disconnected, false, 0, networks)],
            NetworkConnectivity.InternetAccess, true, true, null, NetworkAccessStatus.Allowed, AirplaneModeState.Off);

    private sealed class Session : IDisposable
    {
        public required FakeBackend Backend { get; init; }
        public required ManualEventLoop Loop { get; init; }
        public required NetworkingService Service { get; init; }
        public WifiNetwork Network => Assert.IsType<WifiNetwork>(Assert.Single(Assert.IsType<WifiDevice>(Assert.Single(Service.Devices.Rows)).Networks.Rows));
        public WifiProfile Profile => Assert.IsType<WifiProfile>(Assert.Single(Network.SavedProfiles.Rows));
        public static Session Open(params WifiProfileInfo[] profiles) => Open(Net("Cafe", profiles));
        public static Session Open(params NetworkInfo[] networks) => Open(Shot(networks));
        public static Session Open(NetworkSnapshot snapshot)
        {
            var backend = new FakeBackend();
            var loop = new ManualEventLoop();
            var service = new NetworkingService(backend, loop);
            service.Apply(snapshot);
            return new Session { Backend = backend, Loop = loop, Service = service };
        }
        public void Dispose() => Service.Destroy();
    }

    private sealed class FakeBackend : INetworkBackend
    {
        public NetworkSnapshot Current { get; private set; } = NetworkSnapshot.Initial;
        public List<string> Calls { get; } = [];
        public List<(Guid Device, string Name, bool Auto)> Autos { get; } = [];
        public List<(Guid Device, string Name)> Clears { get; } = [];
        public List<(Guid Device, string Name)> Forgets { get; } = [];
        public List<(Guid Device, string Name, bool Hidden)> Connects { get; } = [];
        public string? AutoError { get; set; }
        public Exception? AutoException { get; set; }
        public TaskCompletionSource<string?>? AutoGate { get; set; }
        public string? ConnectError { get; set; }
        public TaskCompletionSource<string?>? ConnectGate { get; set; }
        public uint ForgetError { get; set; }
        private Action<NetworkSnapshot>? _listener;
        public void Publish(NetworkSnapshot snapshot)
        {
            Current = snapshot;
            _listener?.Invoke(snapshot);
        }
        public IDisposable Subscribe(Action<NetworkSnapshot> listener)
        {
            _listener = listener;
            return new Subscription(this);
        }
        public void Refresh() => Calls.Add("refresh");
        public Task RequestAccessAsync()
        {
            Calls.Add("access");
            return Task.CompletedTask;
        }
        public Task<string?> SetWifiEnabledAsync(bool enabled)
        {
            Calls.Add(enabled ? "wifi-on" : "wifi-off");
            return Task.FromResult<string?>(null);
        }
        public Task ScanAsync(Guid device)
        {
            Calls.Add("scan");
            return Task.CompletedTask;
        }
        public Task<ConnectionFailReason?> ConnectAsync(Guid device, string ssid, string? password)
        {
            Calls.Add("connect");
            return Task.FromResult<ConnectionFailReason?>(null);
        }
        public bool Disconnect(Guid device)
        {
            Calls.Add("disconnect");
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
            public void Dispose() => owner._listener = null;
        }
    }

    private sealed class FakeWlan : IWlanProfileApi
    {
        public string Xml { get; init; } = "";
        public uint Flags { get; init; }
        public uint GetError { get; init; }
        public uint SetError { get; init; }
        public string? WrittenXml { get; private set; }
        public uint WrittenFlags { get; private set; }
        public int Sets { get; private set; }
        public int Connects { get; private set; }
        public int Deletes { get; private set; }
        public uint Get(Guid adapter, string profileName, out string? xml, out uint flags)
        {
            xml = GetError == 0 ? Xml : null;
            flags = Flags;
            return GetError;
        }
        public uint Set(Guid adapter, string xml, uint flags, out uint reason)
        {
            Sets++;
            WrittenXml = xml;
            WrittenFlags = flags;
            reason = 0;
            return SetError;
        }
        public uint Connect(Guid adapter, string profileName, bool hidden)
        {
            Connects++;
            return 0;
        }
        public uint Delete(Guid adapter, string profileName)
        {
            Deletes++;
            return 0;
        }
    }
}
