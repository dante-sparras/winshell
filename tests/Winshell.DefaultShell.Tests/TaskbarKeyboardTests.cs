using Winshell.DefaultShell.Taskbar;

namespace Winshell.DefaultShell.Tests;

public sealed class TaskbarKeyboardTests
{
    [Fact]
    public void Win_T_lands_on_the_first_app_or_the_next_control()
    {
        Assert.Equal(2, TaskbarKeyboard.FirstApp(appCount: 3, count: 8));
        Assert.Equal(2, TaskbarKeyboard.FirstApp(appCount: 0, count: 4));
        Assert.Equal(0, TaskbarKeyboard.FirstApp(appCount: 0, count: 2));
    }

    [Fact]
    public void Arrows_wrap_the_ends()
    {
        Assert.Equal(0, TaskbarKeyboard.Move(0, 1, 0));
        Assert.Equal(3, TaskbarKeyboard.Move(0, -1, 4));
        Assert.Equal(0, TaskbarKeyboard.Move(3, 1, 4));
        Assert.Equal(2, TaskbarKeyboard.Move(1, 1, 4));
    }

    [Fact]
    public void Escape_gives_the_remembered_window_back_and_another_window_gives_nothing()
    {
        var keyboard = new TaskbarKeyboard();
        var first = new Window();
        var later = new Window();

        keyboard.Enter(first);
        keyboard.Enter(later);
        Assert.True(keyboard.Active);
        Assert.Same(first, keyboard.ReturnTo);

        Assert.Same(first, keyboard.Leave());
        Assert.False(keyboard.Active);
        Assert.Null(keyboard.ReturnTo);
        Assert.Null(keyboard.Leave());

        keyboard.Enter(first);
        keyboard.Yield();
        Assert.False(keyboard.Active);
        Assert.Null(keyboard.Leave());
    }

    private sealed class Window : IOpenWindow
    {
        public string AppId => "";

        public string Executable => "";

        public string Title => "";

        public string Icon => "";

        public bool Activated => false;

        public bool Minimized => false;

        public bool Attention => false;

        public IReadOnlyList<ScreenInfo> Screens => [];

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public void Activate()
        {
        }

        public void Minimize()
        {
        }

        public void Restore()
        {
        }

        public void Close()
        {
        }
    }
}
