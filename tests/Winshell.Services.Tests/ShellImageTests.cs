namespace Winshell.Services.Tests;

public sealed class ShellImageTests
{
    [Fact]
    public void A_program_has_an_icon_at_least_the_size_asked_for()
    {
        var image = ShellImage.Load(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), 32);

        Assert.NotNull(image);
        Assert.True(image.Width >= 32 && image.Height >= 32, $"{image.Width}x{image.Height}");
        Assert.True(image.Premultiplied);
        var alpha = image.Bgra.ToArray().Where((_, i) => i % 4 == 3);
        Assert.Contains(alpha, a => a > 0);
    }

    [Fact]
    public void A_path_with_no_item_has_no_icon()
    {
        Assert.Null(ShellImage.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"), 32));
    }
}
