using System.Globalization;
using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class HanFallbackLocaleTests
{
    [Theory]
    [InlineData("zh-CN", "YaHei")]
    [InlineData("zh-Hans-SG", "YaHei")]
    [InlineData("zh-TW", "JhengHei")]
    [InlineData("zh-Hant-HK", "JhengHei")]
    [InlineData("ko-KR", "Malgun")]
    [InlineData("ja-JP", "Yu Gothic")]
    [InlineData("en-US", "Yu Gothic")]
    public void Han_text_falls_back_to_the_font_of_the_ui_language(string culture, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            using var layout = TextLayout.Build(
                [new TextSpan("A中文字", new FontSpec("Segoe UI Variable", 14, 400, false, false, false, 0, 0))],
                double.NaN,
                TextLayout.Wrap.NoWrap,
                TextLayout.Elide.None,
                0,
                1,
                true);
            var han = layout.FontRuns.Where(run => run.Text.Contains('中')).Select(run => run.Family).ToList();
            Assert.Single(han);
            Assert.Contains(expected, han[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
