// Renders packaging/logo.svg into the PNGs the MSIX package and the Store listing use, and into
// packaging/winshell.ico for the executable and the window class. The package PNGs include the
// scale sizes Windows uses on high-DPI screens and the unplated taskbar sizes. Run it after
// changing the logo, and commit the files it writes.
//
//   dotnet run scripts/render-logo.cs
#:property PublishAot=false
#:package Svg.Skia
#:package SkiaSharp
using SkiaSharp;
using Svg.Skia;

var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", ".."));
var packaging = Path.Combine(root, "packaging");
using var svg = new SKSvg();
svg.Load(Path.Combine(packaging, "logo.svg"));
var picture = svg.Picture ?? throw new InvalidOperationException("packaging/logo.svg did not load");

// Name, size in pixels. The unqualified files are the 100% sizes the manifest names. scale-125,
// scale-150, and scale-200 are the sizes Windows looks up; 150 * 1.25 is 188 and 50 * 1.25 is 63.
// The Store file is the listing's app tile.
(string Path, int Size)[] images =
[
    ("msix/Assets/Square44x44Logo.png", 44),
    ("msix/Assets/Square44x44Logo.scale-125.png", 55),
    ("msix/Assets/Square44x44Logo.scale-150.png", 66),
    ("msix/Assets/Square44x44Logo.scale-200.png", 88),
    ("msix/Assets/Square150x150Logo.png", 150),
    ("msix/Assets/Square150x150Logo.scale-125.png", 188),
    ("msix/Assets/Square150x150Logo.scale-150.png", 225),
    ("msix/Assets/Square150x150Logo.scale-200.png", 300),
    ("msix/Assets/StoreLogo.png", 50),
    ("msix/Assets/StoreLogo.scale-125.png", 63),
    ("msix/Assets/StoreLogo.scale-150.png", 75),
    ("msix/Assets/StoreLogo.scale-200.png", 100),
    ("store/logo-300.png", 300),
];
foreach (var (path, size) in images)
    WritePng(Path.Combine(packaging, path), path, Render(picture, size), size);

// Without these, Windows draws the taskbar, Start, and Alt+Tab icon on an accent-coloured plate.
foreach (var size in new[] { 16, 24, 32, 48, 256 })
{
    var path = $"msix/Assets/Square44x44Logo.targetsize-{size}_altform-unplated.png";
    WritePng(Path.Combine(packaging, path), path, Render(picture, size), size);
}

// Shell sizes for the executable icon. 0 in the directory means 256.
int[] iconSizes = [16, 20, 24, 32, 40, 48, 64, 256];
WriteIcon(Path.Combine(packaging, "winshell.ico"), iconSizes.Select(size => Render(picture, size)).ToArray(), iconSizes);
Console.WriteLine($"winshell.ico ({string.Join(", ", iconSizes)})");

static byte[] Render(SKPicture picture, int size)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / picture.CullRect.Width);
        canvas.DrawPicture(picture);
    }
    using var image = SKImage.FromBitmap(bitmap);
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}

static void WritePng(string target, string display, byte[] png, int size)
{
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, png);
    Console.WriteLine($"{display} ({size}x{size})");
}

static void WriteIcon(string target, byte[][] images, int[] sizes)
{
    using var stream = File.Create(target);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Length);
    var offset = 6 + 16 * images.Length;
    for (var i = 0; i < images.Length; i++)
    {
        var side = sizes[i] >= 256 ? 0 : sizes[i];
        writer.Write((byte)side);
        writer.Write((byte)side);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(images[i].Length);
        writer.Write(offset);
        offset += images[i].Length;
    }
    foreach (var image in images)
        writer.Write(image);
}
