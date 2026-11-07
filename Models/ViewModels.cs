using System.Windows;
using System.Windows.Media;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;

namespace DriveSpaceChecker.Models;

public sealed class DriveCardVm
{
    public required string Letter { get; init; }
    public required string Label { get; init; }
    public required string Subtitle { get; init; }
    public required string RootPath { get; init; }
    public required string FreeText { get; init; }
    public required string UsedText { get; init; }
    public required string TotalText { get; init; }
    public required string PercentText { get; init; }
    public required string FreeGbText { get; init; }
    public required string RuleText { get; init; }
    public required string StatusText { get; init; }
    public required double UsedPercent { get; init; }
    public required Brush BadgeBackground { get; init; }
    public required Brush BadgeForeground { get; init; }
    public required Brush StatusBackground { get; init; }
    public required Brush StatusForeground { get; init; }
    public required Brush BarBrush { get; init; }
}

public sealed class DriveRuleVm
{
    public string DriveLetter { get; set; } = "C";
    public bool Enabled { get; set; } = true;
    public int ThresholdPercent { get; set; } = 10;
    public double ThresholdGb { get; set; }
}

public sealed class AlertRecordVm
{
    public AlertRecordVm(AlertRecord record)
    {
        TimeText = record.Utc.ToLocalTime().ToString("dd MMM yyyy HH:mm");
        Summary = record.Summary;
        Error = record.Error;
        StatusText = record.Success ? "Sent" : "Failed";
        StatusBackground = MakeBrush(record.Success ? "#DCFCE7" : "#FEE2E2");
        StatusForeground = MakeBrush(record.Success ? "#166534" : "#991B1B");
        ErrorVisibility = string.IsNullOrWhiteSpace(record.Error) ? Visibility.Collapsed : Visibility.Visible;
    }

    public string TimeText { get; }
    public string Summary { get; }
    public string Error { get; }
    public string StatusText { get; }
    public Brush StatusBackground { get; }
    public Brush StatusForeground { get; }
    public Visibility ErrorVisibility { get; }

    private static Brush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class ContentRow
{
    public FsNode? Node { get; init; }
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Glyph { get; init; }
    public required string SizeText { get; init; }
    public required string PercentText { get; init; }
    public bool IsDirectory { get; init; }
    public Brush CategoryBrush { get; init; } = Brushes.Gray;

    public static ContentRow From(FsNode node)
    {
        return new ContentRow
        {
            Node = node,
            Name = node.Name,
            FullPath = node.FullPath,
            Glyph = node.IsDirectory ? "📁" : "📄",
            SizeText = Format.Bytes(node.Size),
            PercentText = Format.Percent(node.PercentOfParent),
            IsDirectory = node.IsDirectory,
            CategoryBrush = FileCategories.BrushFor(node.Category)
        };
    }
}

public sealed class TypeRow
{
    public required string Name { get; init; }
    public required string SizeText { get; init; }
    public required string PercentText { get; init; }
    public required Brush CategoryBrush { get; init; }
}
