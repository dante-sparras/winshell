namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Windows returns a new object every time the media session list is read. The held session has to stay.
/// </summary>
public class MediaSessionIdentityTests
{
    [Fact]
    public void A_new_object_for_the_same_app_keeps_the_held_session()
    {
        var pair = SmtcSessions.Pair(["Brave"], ["Brave", "testhost.exe"]);
        Assert.Equal([0, -1], pair);
    }

    [Fact]
    public void The_same_app_ids_in_order_stay_paired_when_every_object_is_new()
    {
        var pair = SmtcSessions.Pair(["Brave", "testhost.exe"], ["Brave", "testhost.exe"]);
        Assert.Equal([0, 1], pair);
    }

    [Fact]
    public void Two_sessions_from_one_app_pair_in_list_order()
    {
        var pair = SmtcSessions.Pair(["browser", "browser"], ["browser"]);
        Assert.Equal([0], pair);
    }

    [Fact]
    public void An_app_that_was_not_held_is_a_new_session()
    {
        var pair = SmtcSessions.Pair(["Brave"], ["testhost.exe"]);
        Assert.Equal([-1], pair);
    }
}
