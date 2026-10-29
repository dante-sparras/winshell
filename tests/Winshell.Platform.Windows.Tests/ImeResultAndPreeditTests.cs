using Winshell.Platform.Windows;

namespace Winshell.Platform.Windows.Tests;

/// <summary>One composition message can commit a syllable and start the next preedit.</summary>
public class ImeResultAndPreeditTests
{
    [Fact]
    public void A_result_and_a_new_composition_in_one_message_keeps_the_preedit()
    {
        var session = new ImeSession();
        session.OnComposition(ImeSession.GcsCompStr, "한");

        var actions = session.ReadComposition(ImeSession.GcsResultStr | ImeSession.GcsCompStr, "한", "ㄱ");

        Assert.Equal(2, actions.Length);
        Assert.Equal(ImeActionKind.Commit, actions[0].Kind);
        Assert.Equal("한", actions[0].Text);
        Assert.Equal(ImeActionKind.Preedit, actions[1].Kind);
        Assert.Equal("ㄱ", actions[1].Text);
        Assert.True(session.Composing);
        Assert.Equal("ㄱ", session.Preedit);
        Assert.Equal("한", session.PendingResult);
    }
}
