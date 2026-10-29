namespace Winshell.Runtime.Tests;

public class StructuredValueTests
{
    [Fact]
    public void A_plain_object_assigns_to_point_size_rect_and_a_string_assigns_to_date()
    {
        var root = TestShell.Run("""
            import QtQuick
            Item {
              property point p: ({ x: 2, y: 3 })
              property size s: ({ width: 4, height: 5 })
              property rect r: ({ x: 1, y: 2, width: 3, height: 4 })
              property date d: "2024-01-01"
            }
            """, out var shell);
        using var _ = shell;
        var point = Assert.IsType<QPoint>(root.ReadProperty("p"));
        var size = Assert.IsType<QSize>(root.ReadProperty("s"));
        var rect = Assert.IsType<QRect>(root.ReadProperty("r"));
        var date = Assert.IsType<DateTime>(root.ReadProperty("d"));
        Assert.Equal(2, point.X);
        Assert.Equal(3, point.Y);
        Assert.Equal(4, size.Width);
        Assert.Equal(5, size.Height);
        Assert.Equal(1, rect.X);
        Assert.Equal(2, rect.Y);
        Assert.Equal(3, rect.Width);
        Assert.Equal(4, rect.Height);
        Assert.Equal(new DateTime(2024, 1, 1), date);
    }
}
