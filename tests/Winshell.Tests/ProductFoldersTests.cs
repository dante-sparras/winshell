using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

[TestClass]
public sealed class ProductFoldersTests
{
    [TestMethod]
    public void A_product_finds_another_beside_it()
    {
        ProductFolders.Sibling(@"C:\Users\a\AppData\Local\Winshell\", ProductFolders.Winland)
            .Should().Be(@"C:\Users\a\AppData\Local\Winland");
    }

    [TestMethod]
    public void A_build_folder_finds_its_siblings_the_same_way()
    {
        ProductFolders.Sibling(@"D:\out\Winoma", ProductFolders.Winshell).Should().Be(@"D:\out\Winshell");
    }

    [TestMethod]
    public void Built_in_plugins_come_from_the_first_extension_that_ships_them()
    {
        var folders = new[] { @"D:\out\Plain", @"D:\out\Winoma" };
        ProductFolders.BuiltInPlugins(@"D:\out\Winshell", folders, path => path == @"D:\out\Winoma\plugins")
            .Should().Be(@"D:\out\Winoma\plugins");
    }

    [TestMethod]
    public void A_shell_with_no_extension_reads_its_own_plugins_folder()
    {
        ProductFolders.BuiltInPlugins(@"D:\out\Winshell", [], _ => false).Should().Be(@"D:\out\Winshell\plugins");
    }
}
