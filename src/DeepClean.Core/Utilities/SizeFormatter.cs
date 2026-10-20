using System.Globalization;

namespace DeepClean.Core.Utilities;

/// <summary>Formats byte counts into short, human readable strings.</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string FormatBytes(double bytes, int decimals = 1)
    {
        if (double.IsNaN(bytes) || bytes <= 0)
        {
            return "0 " + Units[0];
        }

        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Clamp(order, 0, Units.Length - 1);

        var value = bytes / Math.Pow(1024, order);
        var format = order == 0 ? "0" : "0." + new string('#', Math.Clamp(decimals, 0, 3));

        return value.ToString(format, CultureInfo.CurrentCulture) + " " + Units[order];
    }

    public static string FormatBytes(long bytes, int decimals = 1) => FormatBytes((double)bytes, decimals);

    public static string FormatBytes(ulong bytes, int decimals = 1) => FormatBytes((double)bytes, decimals);
}
