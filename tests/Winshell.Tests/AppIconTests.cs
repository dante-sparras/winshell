namespace Winshell.Tests;

public class AppIconTests
{
    // The sizes render-logo.cs writes into packaging/winshell.ico.
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    [Fact]
    public void The_logo_icon_contains_each_shell_size()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Repo.Root, "packaging", "winshell.ico"));
        Assert.Equal(Sizes, SizesIn(bytes));
        foreach (var image in ImagesIn(bytes))
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image[..4]);
    }

    [Fact]
    public void The_executable_embeds_the_application_icon()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "winshell.exe");
        // The C# compiler stores ApplicationIcon as icon group 32512, Win32.IDI_APPLICATION.
        using var icon = OwnedIcon.Load(exe, Win32.IDI_APPLICATION, "", 32);
        Assert.NotNull(icon);
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
}
