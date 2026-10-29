using Winshell.Platform.Windows.Native;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// The app bar negotiation against a stand-in for Explorer: ABM_SETPOS grants what is asked, less what another app bar
/// holds on the same edge.
/// </summary>
public class AppBarReservationTests
{
    // A 48 px bar at the bottom of a 1920x1080 monitor, and the same bar above another 40 px bar.
    private static readonly RECT Bottom = new(0, 1032, 1920, 1080);
    private static readonly RECT AboveOther = new(0, 992, 1920, 1040);

    private sealed class Explorer
    {
        public int Requests { get; private set; }

        /// <summary>The height another app bar holds at the bottom, which a grant is moved above.</summary>
        public int Other { get; set; }

        public RECT SetPos(RECT asked)
        {
            Requests++;
            return Other == 0 ? asked : new RECT(asked.Left, asked.Top - Other, asked.Right, asked.Bottom - Other);
        }
    }

    [Fact]
    public void The_first_request_is_asked_for_and_moves_the_work_area()
    {
        var explorer = new Explorer();
        var reservation = new AppBarReservation();
        Assert.True(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.Equal(Bottom, reservation.Granted);
        Assert.Equal(1, explorer.Requests);
    }

    [Fact]
    public void Placing_the_panel_again_for_the_same_reservation_asks_nothing()
    {
        // Explorer announces a new work area after each reservation, which places the panel again: the loop #96 ended.
        var explorer = new Explorer();
        var reservation = new AppBarReservation();
        reservation.Reserve(Bottom, explorer.SetPos);
        for (var i = 0; i < 10; i++)
            Assert.False(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.Equal(1, explorer.Requests);
    }

    [Fact]
    public void A_bar_that_took_the_space_in_between_and_left_again_is_negotiated_back()
    {
        var explorer = new Explorer { Other = 40 };
        var reservation = new AppBarReservation();

        // The query said the edge was free; another bar reserved it before ABM_SETPOS, and Explorer granted less.
        Assert.True(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.Equal(AboveOther, reservation.Granted);

        // While the other bar stays, the query returns what is held, and nothing is asked.
        Assert.False(reservation.Reserve(AboveOther, explorer.SetPos));
        Assert.Equal(1, explorer.Requests);

        // It leaves: the query offers the edge again, which is not what is held, so it is asked for.
        explorer.Other = 0;
        Assert.True(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.Equal(Bottom, reservation.Granted);
        Assert.Equal(2, explorer.Requests);
    }

    [Fact]
    public void A_grant_that_stays_short_of_the_request_does_not_move_the_work_area_again()
    {
        // Explorer keeps granting less than asked: each placement asks once more, but the work area does not move,
        // so it does not start another round of placements.
        var explorer = new Explorer { Other = 40 };
        var reservation = new AppBarReservation();
        Assert.True(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.False(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.False(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.Equal(AboveOther, reservation.Granted);
    }

    [Fact]
    public void After_the_app_bar_is_removed_the_same_rectangle_is_asked_for_again()
    {
        var explorer = new Explorer();
        var reservation = new AppBarReservation();
        reservation.Reserve(Bottom, explorer.SetPos);
        reservation.Clear();
        Assert.True(reservation.Reserve(Bottom, explorer.SetPos));
        Assert.Equal(2, explorer.Requests);
    }
}
