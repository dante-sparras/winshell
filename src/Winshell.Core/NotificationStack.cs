using System.Globalization;

namespace Winshell.Core;

/// <summary>
/// One program's notifications as a stack: its newest card with the rest under it, or every card spread out.
/// </summary>
/// <param name="Command">The command that spreads the stack out or stacks it again.</param>
/// <param name="Count">How many notifications the program has.</param>
/// <param name="Expanded">True while every one of them is shown.</param>
public sealed record NotificationStack(string Command, int Count, bool Expanded)
{
    /// <summary>True when the program has more than one notification, so there is something to stack.</summary>
    public bool Folds => Count > 1;

    /// <summary>How many notifications are under the newest one, out of sight.</summary>
    public int Hidden => Folds && !Expanded ? Count - 1 : 0;

    /// <summary>What the stack's button says: how many more there are, the way Windows notification center counts them, or <c>Show less</c>.</summary>
    public string Label => Expanded
        ? "Show less"
        : "+" + Hidden.ToString(CultureInfo.CurrentCulture) + (Hidden == 1 ? " notification" : " notifications");
}
