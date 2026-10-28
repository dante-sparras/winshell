# Draw the item tree with Skia

Rectangles, text, images, shapes, canvas, and controls are painted with Skia. HarfBuzz shapes the text and Svg.Skia draws SVG. A XAML renderer would split the QML tree from the pixels, so one rasterizer paints every item.
