using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class CreationRollbackTests
{
    [Fact]
    public void A_failed_createObject_leaves_nothing_on_the_parent()
    {
        var root = (Item)TestShell.Run("""
            import QtQuick
            Item {
              id: root
              Component {
                id: bad
                Item {
                  objectName: "outer"
                  Item { objectName: "inner" }
                  NoSuchType { }
                }
              }
              Component.onCompleted: bad.createObject(root)
            }
            """, out var shell);
        using var _ = shell;
        Assert.Empty(root.ChildItems);
        Assert.DoesNotContain(root.OwnedObjects, o => o is Item);
        Assert.Contains(shell.Messages, m => m.Contains("is not a type", StringComparison.Ordinal));
    }
}
