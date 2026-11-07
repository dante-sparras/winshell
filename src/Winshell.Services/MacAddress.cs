using System.Globalization;

namespace Winshell;

/// <summary>A MAC address as the shell shows it: <c>XX:XX:XX:XX:XX:XX</c>.</summary>
internal static class MacAddress
{
    /// <summary>The address in the low six bytes of <paramref name="address"/>, as Bluetooth reports it.</summary>
    public static string Format(ulong address)
    {
        return string.Join(':',
            Enumerable.Range(0, 6).Select(i =>
                ((address >> (8 * (5 - i))) & 0xff).ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static string Format(ReadOnlySpan<byte> bytes)
    {
        return string.Join(':', bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }
}
