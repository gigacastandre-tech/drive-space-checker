using System.IO;
using DriveSpaceChecker.Models;

namespace DriveSpaceChecker.Services;

public static class DriveService
{
    public static List<DriveSnapshot> GetDrives(AppConfig config)
    {
        var result = new List<DriveSnapshot>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            Log.Error("Could not enumerate drives.", ex);
            return result;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                if (!drive.IsReady) continue;
                var name = drive.Name.TrimEnd('\\');
                var rule = GetEffectiveRule(config, name, out var isOverride);
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var snapshot = new DriveSnapshot
                {
                    Name = name,
                    RootPath = drive.RootDirectory.FullName,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    TotalBytes = total,
                    FreeBytes = free,
                    Rule = rule,
                    IsOverride = isOverride
                };
                snapshot.IsTriggered = IsTriggered(config, snapshot, rule);
                snapshot.RuleText = BuildRuleText(config, snapshot, rule, isOverride);
                result.Add(snapshot);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read drive '{drive.Name}': {ex.Message}");
            }
        }

        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static DriveRule GetEffectiveRule(AppConfig config, string driveName, out bool isOverride)
    {
        var letter = ConfigService.NormalizeLetter(driveName.TrimEnd(':', '\\'));
        var rule = config.DriveRules.FirstOrDefault(r =>
            ConfigService.NormalizeLetter(r.DriveLetter) == letter);
        if (rule != null)
        {
            isOverride = true;
            return rule;
        }
        isOverride = false;
        return new DriveRule
        {
            DriveLetter = letter,
            Enabled = true,
            ThresholdPercent = config.Alerts.DefaultThresholdPercent,
            ThresholdGb = config.Alerts.DefaultThresholdGb
        };
    }

    public static bool IsTriggered(AppConfig config, DriveSnapshot drive, DriveRule? rule = null)
    {
        if (!config.Alerts.Enabled) return false;
        rule ??= drive.Rule ?? GetEffectiveRule(config, drive.Name, out _);
        if (!rule.Enabled) return false;
        var percentHit = rule.ThresholdPercent > 0 && drive.FreePercent <= rule.ThresholdPercent;
        var gbHit = rule.ThresholdGb > 0 && drive.FreeGb <= rule.ThresholdGb;
        return percentHit || gbHit;
    }

    private static string BuildRuleText(AppConfig config, DriveSnapshot drive, DriveRule rule, bool isOverride)
    {
        if (!config.Alerts.Enabled) return "Alerts are currently turned off.";
        if (!rule.Enabled) return "Alerts are disabled for this drive.";
        var parts = new List<string>();
        if (rule.ThresholdPercent > 0) parts.Add($"free space ≤ {rule.ThresholdPercent}%");
        if (rule.ThresholdGb > 0) parts.Add($"free space ≤ {rule.ThresholdGb:0.#} GB");
        var text = parts.Count == 0
            ? "No threshold set for this drive."
            : "Email alert when " + string.Join(" or ", parts) + ".";
        return (isOverride ? "Custom rule: " : "Default rule: ") + text;
    }
}
