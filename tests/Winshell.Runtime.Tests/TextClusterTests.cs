using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class TextClusterTests
{
    private const int Left = 0x01000012;
    private const int Right = 0x01000014;
    private const int Backspace = 0x01000003;
    private const int Delete = 0x01000007;

    [Fact]
    public void The_caret_steps_over_an_emoji_and_a_combining_mark()
    {
        var input = new TextInput { Text_ = "a😀b" };
        input.CursorPosition = 0;
        Press(input, Right);
        Assert.Equal(1, input.CursorPosition);
        Press(input, Right);
        Assert.Equal(3, input.CursorPosition);
        Press(input, Right);
        Assert.Equal(4, input.CursorPosition);
        Press(input, Left);
        Assert.Equal(3, input.CursorPosition);
        Press(input, Left);
        Assert.Equal(1, input.CursorPosition);

        input.Text_ = "e\u0301x";
        input.CursorPosition = input.Text_.Length;
        Press(input, Left);
        Assert.Equal(2, input.CursorPosition);
        Press(input, Left);
        Assert.Equal(0, input.CursorPosition);
        input.CursorPosition = 1;
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void Backspace_and_delete_remove_a_whole_cluster()
    {
        var input = new TextInput { Text_ = "a😀b" };
        input.CursorPosition = 3;
        Press(input, Backspace);
        Assert.Equal("ab", input.Text_);
        Assert.Equal(1, input.CursorPosition);
        Assert.Equal("", input.SelectedText);

        input.Text_ = "a😀b";
        input.CursorPosition = 1;
        Press(input, Delete);
        Assert.Equal("ab", input.Text_);
        Assert.Equal(1, input.CursorPosition);

        input.Text_ = "e\u0301";
        input.CursorPosition = 2;
        Press(input, Backspace);
        Assert.Equal("", input.Text_);
    }

    [Fact]
    public void Maximum_length_does_not_cut_a_cluster_in_half()
    {
        var input = new TextInput { MaximumLength = 1 };
        input.Text_ = "😀";
        Assert.Equal("", input.Text_);

        input.MaximumLength = 2;
        input.Text_ = "a😀";
        Assert.Equal("a", input.Text_);

        input.Text_ = "ab";
        input.CursorPosition = 2;
        input.InsertText("😀");
        Assert.Equal("ab", input.Text_);
        Assert.False(HasLoneSurrogate(input.Text_));
    }

    [Fact]
    public void A_password_mask_uses_one_character_per_cluster()
    {
        var input = new TextInput { EchoMode = 2, Text_ = "a😀" };
        Assert.Equal("●●", input.DisplayText);
        Assert.False(HasLoneSurrogate(input.DisplayText));
    }

    [Fact]
    public void Left_and_middle_elision_keep_clusters_whole()
    {
        var emoji = string.Concat(Enumerable.Repeat("😀", 12));
        var font = new FontSpec("Segoe UI Variable", 14, 400, false, false, false, 0, 0);
        foreach (var mode in new[] { TextLayout.Elide.Left, TextLayout.Elide.Middle, TextLayout.Elide.Right })
        {
            using var layout = DirectWriteLayout.Create([new TextSpan(emoji, font)], 30, TextLayout.Wrap.NoWrap, mode, 0, 1, true);
            Assert.False(HasLoneSurrogate(layout.Source), mode.ToString());
            if (mode != TextLayout.Elide.Right)
                Assert.Contains("…", layout.Source);
        }
    }

    private static void Press(TextInput input, int key) => input.KeyPress(new KeyEvent { Key = key });

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return true;
            }
        }
        return false;
    }
}
