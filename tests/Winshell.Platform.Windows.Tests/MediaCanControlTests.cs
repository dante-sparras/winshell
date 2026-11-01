using Winshell.Platform.Windows.Services.Media;

namespace Winshell.Platform.Windows.Tests;

public class MediaCanControlTests
{
    [Fact]
    public void Can_control_is_false_with_no_command_true_with_one_and_false_when_it_goes_away()
    {
        var native = SmtcSession.CreateForTests();
        var session = new MediaSession(native);
        var changes = 0;
        session.Connect("canControlChanged", _ => changes++);

        Assert.False(session.CanControl);
        Assert.Equal(0, changes);

        native.SetControls(canPause: true);
        Assert.True(session.CanControl);
        Assert.Equal(1, changes);

        native.SetControls(canPause: true, canGoNext: true);
        Assert.True(session.CanControl);
        Assert.Equal(1, changes);

        native.SetControls(canGoNext: true);
        Assert.True(session.CanControl);
        Assert.Equal(1, changes);

        native.SetControls();
        Assert.False(session.CanControl);
        Assert.Equal(2, changes);

        native.SetControls(canSeek: true);
        Assert.True(session.CanControl);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Can_control_reports_the_loss_of_a_command_the_session_started_with()
    {
        var native = SmtcSession.CreateForTests();
        native.SetControls(canPlay: true);
        var session = new MediaSession(native);
        var changes = 0;
        session.Connect("canControlChanged", _ => changes++);
        Assert.True(session.CanControl);

        native.SetControls();
        Assert.False(session.CanControl);
        Assert.Equal(1, changes);
    }
}
