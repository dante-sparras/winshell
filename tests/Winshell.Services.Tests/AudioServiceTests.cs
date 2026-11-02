using System.ComponentModel;
using Winshell.Audio;

namespace Winshell.Services.Tests;

public sealed class AudioServiceTests
{
    private readonly ManualEventLoop _loop = new();
    private readonly FakeAudioGraph _graph = new();
    private readonly List<string> _warnings = [];
    private readonly Log _log;

    public AudioServiceTests() => _log = new Log { Sink = (_, _, message) => _warnings.Add(message) };

    private AudioService Service() => new(_loop, _log, _graph);

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, e) => changes.Add(e.PropertyName!);
        return changes;
    }

    [Fact]
    public void The_nodes_and_links_already_there_are_listed()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice, "Speakers"));
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession, "App"));
        _graph.Link(app, speaker);

        using var audio = Service();

        Assert.Equal(["Speakers", "App"], audio.Nodes.Select(n => n.Description));
        var link = Assert.Single(audio.Links);
        Assert.Equal("App", link.Source.Description);
        Assert.Same(audio.Nodes[0], link.Target);
        var group = Assert.Single(audio.LinkGroups);
        Assert.Same(link.Source, group.Source);
        Assert.Same(link.Target, group.Target);
    }

    [Fact]
    public void A_node_that_stays_is_the_same_object()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession));
        var link = _graph.Link(app, speaker);
        using var audio = Service();
        var kept = audio.Nodes[0];

        _graph.Unlink(link);
        _graph.Remove(app);
        _graph.Link(_graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession)), speaker);

        Assert.Equal(2, audio.Nodes.Count);
        Assert.Same(kept, audio.Nodes[0]);
        Assert.Same(kept, Assert.Single(audio.Links).Target);
    }

    [Fact]
    public void A_node_says_what_it_is()
    {
        var channels = new[] { 3, 4, 0x1000 };
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.RecordingSession, "App", channels));
        _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice, "Speakers"));
        using var audio = Service();

        Assert.Equal(app.Id, audio.Nodes[0].Id);
        Assert.Equal(AudioNodeType.RecordingSession, audio.Nodes[0].Type);
        Assert.True(audio.Nodes[0].IsSession);
        Assert.False(audio.Nodes[0].IsPlayback);
        Assert.True(audio.Nodes[0].Ready);
        Assert.Equal([AudioChannel.FrontLeft, AudioChannel.FrontRight, AudioChannel.AuxRangeStart], audio.Nodes[0].Audio.Channels);
        Assert.False(audio.Nodes[1].IsSession);
        Assert.True(audio.Nodes[1].IsPlayback);
    }

    [Fact]
    public void A_new_name_is_reported()
    {
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession, "App"));
        using var audio = Service();
        var changes = Watch(audio.Nodes[0]);

        app.Rename("Player");

        Assert.Equal("Player", audio.Nodes[0].Description);
        Assert.Equal([nameof(AudioNode.Properties), nameof(AudioNode.Description)], changes);
    }

    [Fact]
    public void The_defaults_follow_Windows_and_become_null_with_no_device()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var microphone = _graph.Add(new FakeAudioNode(AudioNodeType.RecordingDevice));
        _graph.SetDefaults(speaker, null);
        using var audio = Service();
        Assert.Same(audio.Nodes[0], audio.DefaultPlaybackDevice);
        Assert.Null(audio.DefaultRecordingDevice);
        var changes = Watch(audio);

        _graph.SetDefaults(speaker, microphone);
        Assert.Same(audio.Nodes[1], audio.DefaultRecordingDevice);
        Assert.Equal([nameof(AudioService.DefaultRecordingDevice)], changes);

        _graph.SetDefaults(null, microphone);
        Assert.Null(audio.DefaultPlaybackDevice);
    }

    [Fact]
    public void Setting_a_default_asks_Windows_and_shows_it_when_Windows_says_so()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        using var audio = Service();

        audio.DefaultPlaybackDevice = audio.Nodes[0];

        Assert.Same(speaker, Assert.Single(_graph.DefaultsAsked));
        Assert.Null(audio.DefaultPlaybackDevice);
    }

    [Theory]
    [InlineData(AudioNodeType.RecordingDevice, true)]
    [InlineData(AudioNodeType.PlaybackSession, true)]
    [InlineData(AudioNodeType.PlaybackDevice, false)]
    [InlineData(AudioNodeType.RecordingSession, false)]
    public void A_node_of_the_wrong_kind_cannot_be_a_default(AudioNodeType type, bool playback)
    {
        _graph.Add(new FakeAudioNode(type));
        using var audio = Service();

        if (playback)
            audio.DefaultPlaybackDevice = audio.Nodes[0];
        else
            audio.DefaultRecordingDevice = audio.Nodes[0];

        Assert.Empty(_graph.DefaultsAsked);
        Assert.Contains("it is not a", Assert.Single(_warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Setting_no_default_changes_nothing()
    {
        using var audio = Service();

        audio.DefaultPlaybackDevice = null;

        Assert.Empty(_graph.DefaultsAsked);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Volume_is_the_average_and_setting_it_keeps_the_balance()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        speaker.SetAudio([0.25f, 0.5f], false);
        using var audio = Service();
        var volume = audio.Nodes[0].Audio;
        Assert.Equal(0.375, volume.Volume, precision: 5);

        volume.Volume = 0.75;

        Assert.Equal(0.5, volume.Volumes[0], precision: 5);
        Assert.Equal(1, volume.Volumes[1], precision: 5);
    }

    [Fact]
    public void Setting_the_volume_of_a_silent_node_sets_every_channel()
    {
        _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        using var audio = Service();

        audio.Nodes[0].Audio.Volume = 0.5;

        Assert.Equal([0.5, 0.5], audio.Nodes[0].Audio.Volumes);
    }

    [Fact]
    public void A_negative_channel_volume_is_zero()
    {
        _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        using var audio = Service();

        audio.Nodes[0].Audio.Volumes = [-1, 0.5];

        Assert.Equal([0, 0.5], audio.Nodes[0].Audio.Volumes);
    }

    [Fact]
    public void A_change_reports_only_the_properties_that_differ()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        speaker.SetAudio([0.25f, 0.75f], false);
        using var audio = Service();
        var volume = audio.Nodes[0].Audio;
        var changes = Watch(volume);

        volume.Muted = true;
        Assert.True(volume.Muted);
        Assert.Equal([nameof(AudioVolume.Muted)], changes);

        changes.Clear();
        speaker.SetAudio([0.75f, 0.25f], true);
        Assert.Equal([nameof(AudioVolume.Volumes)], changes);

        changes.Clear();
        speaker.SetAudio([0.5f, 0.5f], true);
        Assert.Equal([nameof(AudioVolume.Volumes)], changes);

        changes.Clear();
        speaker.SetAudio([0.25f, 0.25f], true);
        Assert.Equal([nameof(AudioVolume.Volumes), nameof(AudioVolume.Volume)], changes);
    }

    [Fact]
    public void A_volume_Windows_refuses_is_logged()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice, "Speakers"));
        using var audio = Service();
        speaker.FailWrites = true;

        audio.Nodes[0].Audio.Muted = true;

        Assert.False(audio.Nodes[0].Audio.Muted);
        Assert.Contains("Cannot change the volume of Speakers", Assert.Single(_warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_and_its_group_follow_the_session()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession));
        var native = _graph.Link(app, speaker);
        using var audio = Service();
        var link = audio.Links[0];
        var group = audio.LinkGroups[0];
        Assert.Equal(AudioLinkState.Inactive, link.State);
        var linkChanges = Watch(link);
        var groupChanges = Watch(group);

        native.Active = true;

        Assert.Equal(AudioLinkState.Active, link.State);
        Assert.Equal(AudioLinkState.Active, group.State);
        Assert.Equal([nameof(AudioLink.State)], linkChanges);
        Assert.Equal([nameof(AudioLinkGroup.State)], groupChanges);
    }

    [Fact]
    public void A_tracker_lists_the_groups_of_its_node_as_they_come_and_go()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var headphones = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession));
        var first = _graph.Link(app, speaker);
        using var audio = Service();
        using var tracker = new AudioLinkTracker { Node = audio.Nodes[0] };
        var kept = Assert.Single(tracker.LinkGroups);

        var other = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession));
        _graph.Link(other, speaker);
        _graph.Link(_graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession)), headphones);
        Assert.Equal(2, tracker.LinkGroups.Count);
        Assert.Same(kept, tracker.LinkGroups[0]);

        _graph.Unlink(first);
        Assert.Equal(audio.Nodes[3], Assert.Single(tracker.LinkGroups).Source);
    }

    [Fact]
    public void A_tracker_lets_go_of_a_node_that_is_removed()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession));
        var link = _graph.Link(app, speaker);
        using var audio = Service();
        using var tracker = new AudioLinkTracker { Node = audio.Nodes[1] };
        Assert.Single(tracker.LinkGroups);
        var changes = Watch(tracker);

        _graph.Unlink(link);
        _graph.Remove(app);

        Assert.Null(tracker.Node);
        Assert.Empty(tracker.LinkGroups);
        Assert.Equal([nameof(AudioLinkTracker.Node)], changes);
    }

    [Fact]
    public void A_disposed_tracker_stops_following_the_service()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        using var audio = Service();
        var tracker = new AudioLinkTracker { Node = audio.Nodes[0] };

        tracker.Dispose();
        _graph.Link(_graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession)), speaker);

        Assert.Empty(tracker.LinkGroups);
    }

    [Fact]
    public void A_disposed_service_stops_following_the_graph()
    {
        var speaker = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        var app = _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackSession));
        var link = _graph.Link(app, speaker);
        var audio = Service();
        var volumeChanges = Watch(audio.Nodes[0].Audio);
        var linkChanges = Watch(audio.Links[0]);
        using var tracker = new AudioLinkTracker { Node = audio.Nodes[0] };

        audio.Dispose();
        _graph.Add(new FakeAudioNode(AudioNodeType.RecordingDevice));
        speaker.SetAudio([1, 1], true);
        link.Active = true;

        Assert.Equal(2, audio.Nodes.Count);
        Assert.Empty(volumeChanges);
        Assert.Empty(linkChanges);
        Assert.Null(tracker.Node);
    }

    [Fact]
    public void An_object_tracker_only_holds_its_list()
    {
        var tracker = new AudioObjectTracker();
        Assert.Empty(tracker.Objects);
        var changes = Watch(tracker);

        tracker.Objects = ["a"];

        Assert.Equal(["a"], tracker.Objects);
        Assert.Equal([nameof(AudioObjectTracker.Objects)], changes);
    }

    private (AudioService Audio, FakeAudioNode Node, FakeMeter Meter) Metered(params float[] peaks)
    {
        var meter = new FakeMeter(peaks);
        var node = _graph.Add(new FakeAudioNode(AudioNodeType.RecordingDevice) { FakeMeter = meter });
        return (Service(), node, meter);
    }

    [Fact]
    public void A_peak_monitor_reads_at_once_and_about_30_times_a_second()
    {
        var (audio, _, meter) = Metered(0.25f, 0.5f);
        using var _ = audio;
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };

        Assert.Equal(0.5, monitor.Peak);
        Assert.Equal([0.25, 0.5], monitor.Peaks);
        Assert.Equal([AudioChannel.FrontLeft, AudioChannel.FrontRight], monitor.Channels);

        var reads = meter.Reads;
        meter.Peaks = [0.75f, 0.5f];
        _loop.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0.75, monitor.Peak);
        Assert.InRange(meter.Reads - reads, 25, 31);
    }

    [Fact]
    public void A_peak_monitor_names_channels_itself_when_the_meter_has_another_count()
    {
        var (audio, _, _) = Metered(0.25f);
        using var _ = audio;
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };

        Assert.Equal([AudioChannel.Mono], monitor.Channels);
    }

    [Fact]
    public void A_disabled_peak_monitor_clears_and_stops_reading()
    {
        var (audio, node, meter) = Metered(0.25f, 0.5f);
        using var _ = audio;
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };
        Assert.Equal(1, node.Running);

        monitor.Enabled = false;

        Assert.Equal(0, monitor.Peak);
        Assert.Empty(monitor.Peaks);
        Assert.Empty(monitor.Channels);
        Assert.Equal(0, node.Running);
        Assert.Null(_loop.NextDue);

        var reads = meter.Reads;
        monitor.Enabled = true;
        Assert.Equal(reads + 1, meter.Reads);
        Assert.Equal(0.5, monitor.Peak);
    }

    [Fact]
    public void A_peak_monitor_on_a_node_with_no_meter_stays_at_zero()
    {
        _graph.Add(new FakeAudioNode(AudioNodeType.PlaybackDevice));
        using var audio = Service();
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };

        Assert.Equal(0, monitor.Peak);
        Assert.Null(_loop.NextDue);
    }

    [Fact]
    public void A_peak_monitor_clears_when_its_node_is_removed()
    {
        var (audio, node, _) = Metered(0.25f, 0.5f);
        using var _ = audio;
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };

        _graph.Remove(node);

        Assert.Equal(0, monitor.Peak);
        Assert.Empty(monitor.Peaks);
        Assert.Equal(0, node.Running);
        Assert.Null(_loop.NextDue);
    }

    [Fact]
    public void A_peak_monitor_keeps_reading_when_the_device_fails_a_read()
    {
        var (audio, _, meter) = Metered(0.25f, 0.5f);
        using var _ = audio;
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };

        meter.Gone = true;
        _loop.Advance(TimeSpan.FromMilliseconds(40));
        meter.Gone = false;
        meter.Peaks = [0.75f, 0.5f];
        _loop.Advance(TimeSpan.FromMilliseconds(40));

        Assert.Equal(0.75, monitor.Peak);
    }

    [Fact]
    public void A_handler_that_turns_the_monitor_off_stops_the_next_read()
    {
        var (audio, _, meter) = Metered(0.25f, 0.5f);
        using var _ = audio;
        using var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };
        monitor.PropertyChanged += (_, _) => monitor.Enabled = false;

        meter.Peaks = [0.75f, 0.5f];
        _loop.Advance(TimeSpan.FromMilliseconds(40));

        Assert.False(monitor.Enabled);
        Assert.Null(_loop.NextDue);
    }

    [Fact]
    public void A_disposed_peak_monitor_lets_go_of_the_device()
    {
        var (audio, node, _) = Metered(0.25f, 0.5f);
        using var _ = audio;
        var monitor = new AudioPeakMonitor(_loop) { Node = audio.Nodes[0] };

        monitor.Dispose();

        Assert.Equal(0, node.Running);
        Assert.Null(monitor.Node);
        Assert.Null(_loop.NextDue);
    }
}
