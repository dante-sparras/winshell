// Renders packaging/logo.svg into the PNGs the MSIX package and the Store listing use. Run it after changing the logo,
// and commit the PNGs it writes.
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

// Name, size in pixels. The package's are the 100% sizes its manifest names; the Store's is the listing's app tile.
(string Path, int Size)[] images =
[
    ("msix/Assets/Square44x44Logo.png", 44),
    ("msix/Assets/Square150x150Logo.png", 150),
    ("msix/Assets/StoreLogo.png", 50),
    ("store/logo-300.png", 300),
];
foreach (var (path, size) in images)
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
    var target = Path.Combine(packaging, path);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, png.ToArray());
    Console.WriteLine($"{path} ({size}x{size})");
}
