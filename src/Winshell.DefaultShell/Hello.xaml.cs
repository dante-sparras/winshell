using Microsoft.UI.Xaml.Controls;

namespace Winshell.DefaultShell;

public sealed partial class Hello : UserControl
{
    public Hello() => InitializeComponent();

    public string Message { get; set; } = "x:Bind works";
}
