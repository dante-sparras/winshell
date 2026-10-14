using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

[TestClass]
public sealed class PluginAssemblyTests
{
    [TestMethod]
    public void An_assembly_in_the_shell_folder_shares_its_context()
    {
        PluginAssembly.SharesShellContext(@"C:\Winoma\Winoma.Plugins.dll", @"C:\Winoma").Should().BeTrue();
        PluginAssembly.SharesShellContext(@"C:\Winoma\plugins\winoma.gallery\Mine.dll", @"C:\Winoma\").Should().BeTrue();
        PluginAssembly.SharesShellContext(@"C:\Winoma-other\Mine.dll", @"C:\Winoma").Should().BeFalse();
        PluginAssembly.SharesShellContext(@"D:\plugins\Mine.dll", @"C:\Winoma").Should().BeFalse();
    }
}
