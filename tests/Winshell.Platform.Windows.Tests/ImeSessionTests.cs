using Winshell.Platform.Windows;

namespace Winshell.Platform.Windows.Tests;

public class ImeSessionTests
{
    [Fact]
    public void Result_string_commits_once_and_the_following_char_is_swallowed()
    {
        var session = new ImeSession();
        var preedit = session.OnComposition(ImeSession.GcsCompStr, "あ");
        Assert.Equal(ImeActionKind.Preedit, preedit.Kind);
        Assert.True(session.Composing);
        Assert.Equal("あ", session.Preedit);

        var commit = session.OnComposition(ImeSession.GcsResultStr, "あ");
        Assert.Equal(ImeActionKind.Commit, commit.Kind);
        Assert.Equal("あ", commit.Text);
        Assert.False(session.Composing);
        Assert.Equal("", session.Preedit);

        var swallowed = session.OnImeChar('あ');
        Assert.Equal(ImeActionKind.Swallow, swallowed.Kind);
        Assert.Equal("", session.PendingResult);

        var end = session.OnEnd();
        Assert.Equal(ImeActionKind.None, end.Kind);
    }

    [Fact]
    public void A_char_without_a_result_string_inserts()
    {
        var session = new ImeSession();
        var inserted = session.OnImeChar('あ');
        Assert.Equal(ImeActionKind.Insert, inserted.Kind);
        Assert.Equal("あ", inserted.Text);
    }

    [Fact]
    public void End_cancels_preedit_and_keeps_a_result_for_the_char_that_follows()
    {
        var session = new ImeSession();
        session.OnComposition(ImeSession.GcsCompStr, "い");
        var cancel = session.OnEnd();
        Assert.Equal(ImeActionKind.Cancel, cancel.Kind);
        Assert.False(session.Composing);
        Assert.Equal("", session.Preedit);

        var commit = session.OnComposition(ImeSession.GcsResultStr | ImeSession.GcsCompStr, "え");
        Assert.Equal(ImeActionKind.Commit, commit.Kind);
        Assert.Equal("え", commit.Text);
        session.OnEnd();
        Assert.Equal(ImeActionKind.Swallow, session.OnImeChar('え').Kind);

        session.ClearPending();
        Assert.Equal(ImeActionKind.Insert, session.OnImeChar('え').Kind);
    }

    [Fact]
    public void An_empty_composition_cancels_only_the_preedit()
    {
        var session = new ImeSession();
        session.OnComposition(ImeSession.GcsCompStr, "う");
        session.OnComposition(ImeSession.GcsResultStr, "う");
        var cancel = session.OnComposition(0, "");
        Assert.Equal(ImeActionKind.None, cancel.Kind);
        Assert.Equal("う", session.PendingResult);
    }

    [Fact]
    public void Set_context_hides_the_default_composition_window()
    {
        var shown = unchecked((nint)(0x80000000 | 1));
        Assert.Equal((nint)1, ImeSession.HideDefaultCompositionWindow(shown));
    }
}
