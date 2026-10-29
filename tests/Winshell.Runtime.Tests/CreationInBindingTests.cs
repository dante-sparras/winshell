namespace Winshell.Runtime.Tests;

public class CreationInBindingTests
{
    [Fact]
    public void A_binding_that_creates_an_object_does_not_depend_on_its_reads()
    {
        var root = TestShell.Run("""
            import QtQml
            QtObject {
              id: root
              property int source: 1
              property Component maker: Component {
                QtObject {
                  property int seen: 0
                  Component.onCompleted: seen = root.source
                }
              }
              property QtObject built: maker.createObject(root)
            }
            """, out var shell);
        using (shell)
        {
            var built = (QObject)root.ReadProperty("built")!;
            Assert.Equal(1, built.ReadProperty("seen"));

            root.WriteProperty("source", 2);

            Assert.Same(built, root.ReadProperty("built"));
        }
    }
}
