# Draw the item tree with Skia

Rectangles, text, images, shapes, canvas, and controls are painted with Skia. HarfBuzz shapes the text and Svg.Skia draws SVG. A XAML renderer would split the QML tree from the pixels, so one rasterizer paints every item.

ADR 0005 narrows the text part of this decision. Text is drawn with DirectWrite, and the built-in controls are exposed to UI Automation. Shapes, images, and control chrome stay on this Skia scene, and a XAML renderer stays rejected.
