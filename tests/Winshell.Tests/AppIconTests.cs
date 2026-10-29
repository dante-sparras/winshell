using System.Runtime.InteropServices;

namespace Winshell.Tests;

public class AppIconTests
{
    // The sizes render-logo.cs writes into packaging/winshell.ico.
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [Fact]
    public void The_logo_icon_contains_each_shell_size()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "packaging", "winshell.ico"));
        Assert.Equal(Sizes, SizesIn(bytes));
        foreach (var image in ImagesIn(bytes))
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image[..4]);
    }

    [Fact]
    public void The_executable_embeds_the_application_icon()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");
        // LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE. The file is not executed.
        var module = LoadLibraryExW(exe, 0, 0x22);
        Assert.NotEqual(0, module);
        try
        {
            var names = new List<nint>();
            var callback = new EnumResNameProc((_, _, name, _) =>
            {
                names.Add(name);
                return true;
            });
            // RT_GROUP_ICON is 14. The C# compiler stores ApplicationIcon as 32512, Win32.IDI_APPLICATION.
            Assert.True(EnumResourceNamesW(module, 14, callback, 0));
            Assert.Contains((nint)32512, names);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static int[] SizesIn(byte[] bytes)
    {
        var count = Count(bytes);
        var sizes = new int[count];
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            sizes[i] = bytes[entry] == 0 ? 256 : bytes[entry];
            var height = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
            Assert.Equal(sizes[i], height);
        }
        return sizes;
    }

    private static IEnumerable<byte[]> ImagesIn(byte[] bytes)
    {
        var count = Count(bytes);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);
            yield return bytes[offset..(offset + length)];
        }
    }

    private static int Count(byte[] bytes)
    {
        Assert.True(bytes.Length >= 6);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 0));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        return BitConverter.ToUInt16(bytes, 4);
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

    private delegate bool EnumResNameProc(nint module, nint type, nint name, nint param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string file, nint reserved, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumResourceNamesW(nint module, nint type, EnumResNameProc proc, nint param);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);
}
