using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DriveSpaceChecker.Models;

namespace DriveSpaceChecker.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    public ConfigService()
    {
        Current = Load();
        Normalize(Current);
    }

    public AppConfig Current { get; }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var json = File.ReadAllText(AppPaths.ConfigFile);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings; starting with defaults.", ex);
        }
        return new AppConfig();
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                AppPaths.EnsureDirectories();
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var temp = AppPaths.ConfigFile + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, AppPaths.ConfigFile, true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings.", ex);
            }
        }
    }

    /// <summary>Thread-safe update from background services.</summary>
    public void Mutate(Action<AppConfig> action)
    {
        lock (_gate)
        {
            action(Current);
            try
            {
                AppPaths.EnsureDirectories();
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var temp = AppPaths.ConfigFile + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, AppPaths.ConfigFile, true);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings.", ex);
            }
        }
    }

    private static void Normalize(AppConfig cfg)
    {
        cfg.Smtp ??= new SmtpSettings();
        cfg.Alerts ??= new AlertSettings();
        cfg.General ??= new GeneralSettings();
        cfg.DriveRules ??= new List<DriveRule>();
        cfg.History ??= new List<AlertRecord>();
        cfg.Alerts.LastAlerts ??= new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cfg.Smtp.FromName)) cfg.Smtp.FromName = "Drive Space Checker";
        if (cfg.Alerts.CheckIntervalMinutes < 1) cfg.Alerts.CheckIntervalMinutes = 15;
        if (cfg.Alerts.CooldownHours < 0) cfg.Alerts.CooldownHours = 12;
        if (cfg.Alerts.DefaultThresholdPercent is < 0 or > 100) cfg.Alerts.DefaultThresholdPercent = 10;
        if (cfg.Alerts.DailyReportHour is < 0 or > 23) cfg.Alerts.DailyReportHour = 8;
        foreach (var rule in cfg.DriveRules)
        {
            rule.DriveLetter = NormalizeLetter(rule.DriveLetter);
            rule.ThresholdPercent = Math.Clamp(rule.ThresholdPercent, 0, 100);
            if (rule.ThresholdGb < 0) rule.ThresholdGb = 0;
        }
        cfg.DriveRules = cfg.DriveRules
            .GroupBy(r => r.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (cfg.History.Count > 100)
        {
            cfg.History = cfg.History.OrderByDescending(h => h.Utc).Take(100).ToList();
        }
    }

    public static string NormalizeLetter(string? letter)
    {
        if (string.IsNullOrWhiteSpace(letter)) return "C";
        var cleaned = new string(letter.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cleaned.Length > 1) cleaned = cleaned[..1];
        return cleaned.Length == 0 ? "C" : cleaned;
    }
}
