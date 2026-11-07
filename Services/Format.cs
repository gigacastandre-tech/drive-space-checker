namespace DriveSpaceChecker.Services;

public static class Format
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        if (unit == 0) return $"{bytes:N0} B";
        var decimals = value >= 100 ? 0 : value >= 10 ? 1 : 2;
        return $"{value.ToString("N" + decimals)} {Units[unit]}";
    }

    public static string Percent(double percent) => $"{percent:0.#}%";

    public static string Count(long value) => value.ToString("N0");

    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }
}
