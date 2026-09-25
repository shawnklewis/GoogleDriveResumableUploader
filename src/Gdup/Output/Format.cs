namespace Gdup.Output;

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(double bytes)
    {
        var unit = 0;
        while (bytes >= 1000 && unit < Units.Length - 1)
        {
            bytes /= 1000;
            unit++;
        }
        return unit == 0 ? $"{bytes:0} B" : $"{bytes:0.0} {Units[unit]}";
    }

    public static string Rate(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Duration(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
}
