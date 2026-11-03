using Microsoft.UI.Xaml.Controls;
using Winshell.Native;

namespace Winshell.ShellTests;

[Trait("Category", "Slow")]
public sealed class PanelMenuTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_panel_press_requests_native_menu_dismissal_without_taking_activation(bool mouseActivation)
    {
        await ShellFixture.On(shell =>
        {
            var dismissalRequested = false;
            using var panel = new PanelWindow { Content = new Grid() };
            // Native menu cancellation is the Windows boundary: no foreign process or real input is needed.
            using var host = new HostWindow(shell.Platform, WindowKind.Panel, panel, () => dismissalRequested = true);
            var foreground = Win32.GetForegroundWindow();

            if (mouseActivation)
                Assert.Equal(Win32.MA_NOACTIVATE, Win32.SendMessageW(host.Handle, Win32.WM_MOUSEACTIVATE, 0, 0));
            else
                host.Press();

            Assert.True(dismissalRequested);
            Assert.Equal(foreground, Win32.GetForegroundWindow());
            return true;
        });
    }
}
