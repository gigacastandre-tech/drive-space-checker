namespace DriveSpaceChecker.Models;

public sealed class DriveSnapshot
{
    public string Name { get; init; } = "";
    public string RootPath { get; init; } = "";
    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double FreePercent => TotalBytes <= 0 ? 0 : FreeBytes * 100.0 / TotalBytes;
    public double UsedPercent => TotalBytes <= 0 ? 100 : UsedBytes * 100.0 / TotalBytes;
    public double FreeGb => FreeBytes / 1024.0 / 1024.0 / 1024.0;
    public bool IsTriggered { get; set; }
    public bool IsOverride { get; set; }
    public DriveRule? Rule { get; set; }
    public string RuleText { get; set; } = "";
}
