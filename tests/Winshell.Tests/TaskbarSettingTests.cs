using FluentAssertions;
using Winshell.Core;

namespace Winshell.Tests;

[TestClass]
public sealed class TaskbarSettingTests
{
    [TestMethod]
    public void Only_hidden_hides_the_taskbar()
    {
        TaskbarSetting.Hides(TaskbarSetting.Hidden).Should().BeTrue();
        TaskbarSetting.Hides(TaskbarSetting.Shown).Should().BeFalse();
        TaskbarSetting.Hides(null).Should().BeFalse();
        TaskbarSetting.Hides("").Should().BeFalse();
        TaskbarSetting.Hides("Hidden").Should().BeFalse();
    }
}
