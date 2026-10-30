using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

public class FileUrlPathTests
{
    [Fact]
    public void A_file_url_with_a_host_names_a_unc_path()
    {
        Assert.Equal(@"\\host\share\x.png", ImageLoader.LocalPath("file://host/share/x.png", @"C:\config"));
    }

    [Fact]
    public void A_file_url_with_escapes_names_the_unescaped_path()
    {
        Assert.Equal(@"C:\x y.png", ImageLoader.LocalPath("file:///C:/x%20y.png", @"C:\config"));
    }

    [Fact]
    public void A_file_url_with_a_drive_names_the_drive_path()
    {
        Assert.Equal(@"C:\pictures\x.png", ImageLoader.LocalPath("file:///C:/pictures/x.png", @"C:\config"));
    }

    [Fact]
    public void A_relative_path_is_relative_to_the_base_directory()
    {
        Assert.Equal(@"C:\config\images\x.png", Path.GetFullPath(ImageLoader.LocalPath("images/x.png", @"C:\config")!));
    }
}
