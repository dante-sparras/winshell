using System.Text;

namespace Winshell.Core;

/// <summary>Identity of a bar face. An unchanged mark means the slot can skip the paint.</summary>
public static class BarFaceMark
{
    /// <summary>A stable mark of <paramref name="face"/>, including label, tooltip, text width, picture, chips, and tray icons.</summary>
    /// <param name="face">The face a slot would draw.</param>
    /// <returns>The mark. Hidden faces share one mark.</returns>
    public static string Of(BarFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (!face.Visible)
        {
            return "0";
        }

        var builder = new StringBuilder(128);
        builder.Append(face.Tray ? 't' : 'b');
        builder.Append('\u001f');
        builder.Append(face.Glyph);
        builder.Append('\u001f');
        builder.Append(ChipMark.Picture(face.Picture));
        builder.Append('\u001f');
        builder.Append(face.Text);
        builder.Append('\u001f');
        builder.Append(face.TextWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append('\u001f');
        builder.Append(face.Label);
        builder.Append('');
        builder.Append(face.Tooltip);
        builder.Append('\u001f');
        builder.Append(face.Accent ? '1' : '0');
        builder.Append(face.Quiet ? '1' : '0');
        builder.Append((int)face.Tone);
        builder.Append(face.TrayVertical ? '1' : '0');
        builder.Append(face.TrayExpanded ? '1' : '0');
        builder.Append('\u001f');
        // A chip can keep its tag and change its text, and the chip mark leaves the text out.
        Append(builder, face.Chips);
        builder.Append('\u001f');
        Append(builder, face.TrayDrawer);
        builder.Append('\u001f');
        Append(builder, face.TrayPinned);
        builder.Append('\u001f');
        foreach (var readout in face.Readouts)
        {
            builder.Append(readout.Glyph);
            builder.Append(':');
            builder.Append(readout.Label);
            builder.Append(':');
            builder.Append(readout.Value);
            builder.Append(',');
        }

        builder.Append('\u001f');
        foreach (var meter in face.Meters)
        {
            builder.Append(meter.Mark);
            builder.Append(':');
            builder.Append(meter.Figure);
            builder.Append(':');
            builder.Append(Math.Round(meter.Fill * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(Math.Round(meter.Tick * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append((int)meter.Tone);
            builder.Append((int)meter.Shows);
            builder.Append(',');
        }

        return builder.ToString();
    }

    // A tray icon's tag is its place in the row, so the tag alone cannot tell one program from the one that took its place.
    // The key and the name tell them apart, and the picture's own bytes tell a new picture of the same size from the old.
    private static void Append(StringBuilder builder, IReadOnlyList<BarChip> chips)
    {
        foreach (var chip in chips)
        {
            builder.Append(chip.Key);
            builder.Append(':');
            builder.Append(chip.Text);
            builder.Append(',');
        }

        builder.Append(ChipMark.Of(chips));
    }
}
