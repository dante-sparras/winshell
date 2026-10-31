using SkiaSharp;
using Winshell.Runtime;
using Winshell.Runtime.Quick;

namespace Winshell.Runtime.Tests;

/// <summary>Qt Quick members that used to load and then do nothing.</summary>
public class QtQuickGapTests
{
    [Fact]
    public void A_drag_past_the_edge_moves_halfway_and_returns_on_release()
    {
        var flickable = FlickableOf();
        Drag(flickable, 10, 50);
        Assert.Equal(-20, flickable.ContentY);
        flickable.PointerRelease(10, 50, QtEnums.LeftButton, 0, 0);
        Assert.Equal(0, flickable.ContentY);

        flickable.BoundsBehavior = (int)Flickable.BoundsBehaviorValue.StopAtBounds;
        Drag(flickable, 10, 50);
        Assert.Equal(0, flickable.ContentY);

        flickable.BoundsBehavior = (int)Flickable.BoundsBehaviorValue.DragAndOvershootBounds;
        flickable.BoundsMovement = 0;
        Drag(flickable, 10, 50);
        Assert.Equal(0, flickable.ContentY);

        flickable.BoundsMovement = (int)Flickable.BoundsMovementValue.FollowBoundsBehavior;
        flickable.BoundsBehavior = (int)Flickable.BoundsBehaviorValue.OvershootBounds;
        Drag(flickable, 10, 50);
        Assert.Equal(0, flickable.ContentY);
        flickable.Destroy();
    }

    [Fact]
    public void Pixel_aligned_content_rounds_half_away_from_zero()
    {
        var flickable = FlickableOf();
        flickable.ContentX = 1.6;
        Assert.Equal(1.6, flickable.ContentX);
        flickable.PixelAligned = true;
        Assert.Equal(2, flickable.ContentX);
        flickable.ContentX = 1.4;
        Assert.Equal(1, flickable.ContentX);
        flickable.ContentY = -1.5;
        Assert.Equal(-2, flickable.ContentY);
        flickable.PixelAligned = false;
        flickable.ContentX = 1.6;
        Assert.Equal(1.6, flickable.ContentX);
        flickable.Destroy();
    }

    [Fact]
    public void Grid_and_flow_place_right_to_left()
    {
        var grid = new Grid { Columns = 1, LayoutDirection = 1 };
        var narrow = Child(grid, 10, 10);
        var wide = Child(grid, 20, 10);
        grid.ForceLayout();
        Assert.Equal(10, narrow.X);
        Assert.Equal(0, wide.X);

        var flow = new Flow { Width = 100, LayoutDirection = 1 };
        var first = Child(flow, 10, 10);
        var second = Child(flow, 10, 10);
        flow.ForceLayout();
        Assert.Equal(90, first.X);
        Assert.Equal(80, second.X);

        var left = new Flow { Width = 100 };
        var a = Child(left, 10, 10);
        var b = Child(left, 10, 10);
        left.ForceLayout();
        Assert.Equal(0, a.X);
        Assert.Equal(10, b.X);
        grid.Destroy();
        flow.Destroy();
        left.Destroy();
    }

    [Fact]
    public void An_image_mirrors_vertically_and_clips_its_source()
    {
        var image = Load(4, 2, (x, y) => y == 0
            ? x switch { 0 => SKColors.Red, 1 => SKColors.Green, 2 => SKColors.Blue, _ => SKColors.Yellow }
            : x switch { 0 => SKColors.White, 1 => SKColors.Black, 2 => SKColors.Cyan, _ => SKColors.Magenta });
        Assert.Equal(SKColors.Red, Pixel(image, 0, 0));
        image.MirrorVertically = true;
        Assert.Equal(SKColors.Cyan, Pixel(image, 2, 0));
        image.MirrorVertically = false;
        image.SourceClipRect = new QRect(2, 0, 2, 2);
        image.Width = image.ImplicitWidth;
        image.Height = image.ImplicitHeight;
        Assert.Equal(2, image.ImplicitWidth);
        Assert.Equal(SKColors.Blue, Pixel(image, 0, 0));
        Assert.Equal(SKColors.Yellow, Pixel(image, 1, 0));
        image.SourceClipRect = new QRect(0, 0, 0, 0);
        Assert.Equal(4, image.ImplicitWidth);
        image.Destroy();
    }

    private static Flickable FlickableOf()
    {
        var flickable = new Flickable { Width = 100, Height = 100, ContentHeight = 1000 };
        flickable.MarkComplete();
        return flickable;
    }

    private static void Drag(Flickable flickable, double x, double y)
    {
        flickable.PointerPress(10, 10, QtEnums.LeftButton, QtEnums.LeftButton, 0);
        flickable.PointerMove(x, y, QtEnums.LeftButton, 0);
    }

    private static Item Child(Item parent, double width, double height)
    {
        var item = new Item { Width = width, Height = height };
        item.SetParentItem(parent);
        return item;
    }

    private static Image Load(int width, int height, Func<int, int, SKColor> pixel)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, pixel(x, y));
        using var picture = SKImage.FromBitmap(bitmap);
        using var encoded = picture.Encode(SKEncodedImageFormat.Png, 100);
        var image = new Image { Source = "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray()), Smooth = false };
        image.MarkComplete();
        image.Width = image.ImplicitWidth;
        image.Height = image.ImplicitHeight;
        return image;
    }

    private static SKColor Pixel(Image image, int x, int y)
    {
        using var surface = SKSurface.Create(new SKImageInfo((int)image.Width, (int)image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface!.Canvas.Clear(SKColors.Transparent);
        image.Paint(surface.Canvas, new PaintContext());
        using var snap = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(snap);
        return bitmap.GetPixel(x, y);
    }
}
