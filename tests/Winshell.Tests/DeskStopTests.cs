using FluentAssertions;

namespace Winshell.Tests;

/// <summary>winshell stop leaves Winland up. winoma stop asks Winland to stop (ADR 0067).</summary>
[TestClass]
public sealed class DeskStopTests
{
    [TestMethod]
    public void Stopping_the_shell_does_not_stop_Winland()
    {
        DeskStop.WinlandCommand(desktop: false, replaced: false).Should().BeNull();
        DeskStop.WinlandCommand(desktop: false, replaced: true).Should().BeNull();
    }

    [TestMethod]
    public void Stopping_the_desktop_stops_Winland_and_a_replace_leaves_its_windows_hidden()
    {
        DeskStop.WinlandCommand(desktop: true, replaced: false).Should().Be("stop");
        DeskStop.WinlandCommand(desktop: true, replaced: true).Should().Be("replace");
    }
}
