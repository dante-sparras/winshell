using System.Buffers.Binary;

namespace Winshell.Tests;

public class MsixLogoTests
{
    [Fact]
    public void The_package_logos_include_scale_and_unplated_sizes()
    {
        var assets = Path.Combine(RepoRoot(), "packaging", "msix", "Assets");
        AssertSize(assets, "Square44x44Logo.png", 44);
        AssertSize(assets, "Square44x44Logo.scale-125.png", 55);
        AssertSize(assets, "Square44x44Logo.scale-150.png", 66);
        AssertSize(assets, "Square44x44Logo.scale-200.png", 88);
        AssertSize(assets, "Square150x150Logo.png", 150);
        AssertSize(assets, "Square150x150Logo.scale-125.png", 188);
        AssertSize(assets, "Square150x150Logo.scale-150.png", 225);
        AssertSize(assets, "Square150x150Logo.scale-200.png", 300);
        AssertSize(assets, "StoreLogo.png", 50);
        AssertSize(assets, "StoreLogo.scale-125.png", 63);
        AssertSize(assets, "StoreLogo.scale-150.png", 75);
        AssertSize(assets, "StoreLogo.scale-200.png", 100);
        foreach (var size in new[] { 16, 24, 32, 48, 256 })
            AssertSize(assets, $"Square44x44Logo.targetsize-{size}_altform-unplated.png", size);
    }

    private static void AssertSize(string directory, string name, int size)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, name));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("The tests run outside the repository.");
    }
}
