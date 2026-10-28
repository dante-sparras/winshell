using System.Globalization;
using Winshell.Platform.Windows.Services.Media;

namespace Winshell.Platform.Windows.Tests;

public class ArtCacheTests
{
    [Fact]
    public void Art_is_kept_in_a_folder_of_this_process_and_deleted_after_its_last_use()
    {
        var art = Guid.NewGuid().ToByteArray();
        var url = ArtCache.Store(art, "image/png");
        var path = new Uri(url).LocalPath;
        // Another instance deleting its copy of the same art must not take this one's.
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(url, ArtCache.Store(art, "image/png"));

        ArtCache.Release(url);
        Assert.True(File.Exists(path));
        ArtCache.Release(url);
        Assert.False(File.Exists(path));
    }
}
