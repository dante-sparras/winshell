using Winshell.Runtime;
using Xunit;

namespace Winshell.Runtime.Tests;

public class WideEnumTests
{
    [Fact]
    public void An_empty_signal_name_has_a_handler_name()
    {
        Assert.Equal("on", Names.HandlerName(""));
        Assert.Equal("onClicked", Names.HandlerName("clicked"));
    }

    [Fact]
    public void A_uint_or_long_enum_with_a_large_value_registers_and_round_trips()
    {
        using var shell = new TestShell(("shell.qml", """
            import Test
            Flagged {
              property bool same: mode === Flagged.High
              property int far: Flagged.Far
              property int wide: Wide.Far
            }
            """));
        shell.Engine.Modules.Module("Test").Type<Flagged>().Uncreatable<Flagged.Wide>("Wide");
        var root = (Flagged)shell.Load();

        Assert.True((bool)root.ReadProperty("same")!);
        Assert.Equal(5, root.ReadProperty("far"));
        Assert.Equal(5, root.ReadProperty("wide"));
        shell.Eval(root, "mode = Flagged.Low");
        Assert.Equal(Flagged.Big.Low, root.Mode);
        shell.Eval(root, "mode = Flagged.High");
        Assert.Equal(Flagged.Big.High, root.Mode);
        Assert.Empty(shell.Messages);
    }

    private sealed class Flagged : QObject
    {
        private Big _mode = Big.High;

        [QmlEnum]
        public enum Big : uint
        {
            Low = 1,
            High = 0x80000000,
        }

        [QmlEnum]
        public enum Wide : long
        {
            Far = (1L << 40) | 5,
        }

        [Qml]
        public Big Mode
        {
            get { Track(); return _mode; }
            set => SetField(ref _mode, value);
        }
    }
}
