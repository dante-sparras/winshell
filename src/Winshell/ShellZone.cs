using System.Globalization;
using System.Runtime.InteropServices;

namespace Winshell;

/// <summary>The time zone Windows is in now, asked without .NET's kept copy.</summary>
internal static class ShellZone
{
    /// <summary>
    /// Names the zone and its offsets, for <see cref="ZoneWatch"/>. Two answers differ when the zone, its offsets,
    /// or whether it follows daylight time changed. One call, with no registry read.
    /// </summary>
    /// <returns>The zone's key name and offsets, or empty when Windows did not answer.</returns>
    internal static string Key()
    {
        if (GetDynamicTimeZoneInformation(out var zone) == Invalid)
        {
            return "";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{zone.KeyName}|{zone.Bias}|{zone.StandardBias}|{zone.DaylightBias}|{zone.DaylightDisabled}");
    }

    private const uint Invalid = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern uint GetDynamicTimeZoneInformation(out DynamicTimeZone zone);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }

    // DYNAMIC_TIME_ZONE_INFORMATION. Each date is a SYSTEMTIME.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DynamicTimeZone
    {
        internal int Bias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string StandardName;

        internal SystemTime StandardDate;
        internal int StandardBias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DaylightName;

        internal SystemTime DaylightDate;
        internal int DaylightBias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string KeyName;

        internal byte DaylightDisabled;
    }
}
