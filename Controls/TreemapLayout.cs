using System.Windows;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;

namespace DriveSpaceChecker.Controls;

public sealed class TreemapItem
{
    public required string Name { get; init; }
    public required long Size { get; init; }
    public FsNode? Node { get; init; }
    public bool IsDirectory { get; init; }
    public int Category { get; init; }
    public bool IsFree { get; init; }
    public bool IsAggregate { get; init; }
    public double Percent { get; init; }
}

public sealed class TreemapSlice
{
    public required TreemapItem Item { get; init; }
    public Rect Rect { get; init; }
}

public static class TreemapLayout
{
    public static List<TreemapItem> BuildItems(FsNode current, ScanResult result, int maxItems = 220)
    {
        var items = new List<TreemapItem>();
        var free = current == result.Root && result.IsDriveRoot ? result.DriveFreeBytes : 0;
        var children = current.Children?
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList() ?? new List<FsNode>();

        var total = children.Sum(c => c.Size) + free;
        if (total <= 0) return items;

        var shown = children;
        long otherSize = 0;
        int otherCount = 0;
        if (children.Count > maxItems)
        {
            shown = children.Take(maxItems - 1).ToList();
            otherSize = children.Skip(maxItems - 1).Sum(c => c.Size);
            otherCount = children.Count - (maxItems - 1);
        }

        foreach (var child in shown)
        {
            items.Add(new TreemapItem
            {
                Name = child.Name,
                Size = child.Size,
                Node = child,
                IsDirectory = child.IsDirectory,
                Category = child.Category,
                Percent = child.Size * 100.0 / total
            });
        }

        if (otherSize > 0)
        {
            items.Add(new TreemapItem
            {
                Name = $"Other ({otherCount:N0} items)",
                Size = otherSize,
                Category = FileCategories.Other,
                IsAggregate = true,
                Percent = otherSize * 100.0 / total
            });
        }

        if (free > 0)
        {
            items.Add(new TreemapItem
            {
                Name = "Free space",
                Size = free,
                Category = FileCategories.Free,
                IsFree = true,
                Percent = free * 100.0 / total
            });
        }

        return items;
    }

    public static List<TreemapSlice> Layout(IReadOnlyList<TreemapItem> items, Rect bounds)
    {
        var result = new List<TreemapSlice>(items.Count);
        if (items.Count == 0 || bounds.Width <= 1 || bounds.Height <= 1) return result;

        double total = items.Sum(i => (double)i.Size);
        if (total <= 0) return result;

        var areas = items.Select(i => i.Size / total * bounds.Width * bounds.Height).ToList();
        var remaining = bounds;
        var index = 0;
        while (index < items.Count && remaining.Width > 0.5 && remaining.Height > 0.5)
        {
            var rowEnd = index;
            double rowSum = 0;
            double worst = double.MaxValue;
            while (rowEnd < items.Count)
            {
                var candidateSum = rowSum + areas[rowEnd];
                var candidateWorst = WorstAspect(areas, index, rowEnd + 1, candidateSum, remaining);
                if (rowEnd > index && candidateWorst > worst) break;
                rowSum = candidateSum;
                worst = candidateWorst;
                rowEnd++;
            }

            LayoutRow(items, areas, index, rowEnd, rowSum, ref remaining, result);
            index = rowEnd;
        }

        return result;
    }

    private static double WorstAspect(List<double> areas, int start, int end, double sum, Rect rect)
    {
        if (sum <= 0) return double.MaxValue;
        var side = Math.Min(rect.Width, rect.Height);
        if (side <= 0) return double.MaxValue;
        var thickness = sum / side;
        if (thickness <= 0) return double.MaxValue;
        var worst = 0.0;
        for (var i = start; i < end; i++)
        {
            var length = areas[i] / thickness;
            if (length <= 0) continue;
            var ratio = Math.Max(thickness / length, length / thickness);
            if (ratio > worst) worst = ratio;
        }
        return worst;
    }

    private static void LayoutRow(IReadOnlyList<TreemapItem> items, List<double> areas, int start, int end,
        double rowSum, ref Rect remaining, List<TreemapSlice> output)
    {
        if (rowSum <= 0 || end <= start) return;
        if (remaining.Width >= remaining.Height)
        {
            var width = Math.Min(remaining.Width, rowSum / remaining.Height);
            var y = remaining.Y;
            for (var i = start; i < end; i++)
            {
                var height = width <= 0 ? 0 : Math.Min(remaining.Y + remaining.Height - y, areas[i] / width);
                output.Add(new TreemapSlice
                {
                    Item = items[i],
                    Rect = new Rect(remaining.X, y, width, Math.Max(0, height))
                });
                y += height;
            }
            remaining = new Rect(remaining.X + width, remaining.Y, Math.Max(0, remaining.Width - width), remaining.Height);
        }
        else
        {
            var height = Math.Min(remaining.Height, rowSum / remaining.Width);
            var x = remaining.X;
            for (var i = start; i < end; i++)
            {
                var width = height <= 0 ? 0 : Math.Min(remaining.X + remaining.Width - x, areas[i] / height);
                output.Add(new TreemapSlice
                {
                    Item = items[i],
                    Rect = new Rect(x, remaining.Y, Math.Max(0, width), height)
                });
                x += width;
            }
            remaining = new Rect(remaining.X, remaining.Y + height, remaining.Width, Math.Max(0, remaining.Height - height));
        }
    }
}
