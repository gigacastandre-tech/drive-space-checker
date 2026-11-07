namespace DriveSpaceChecker.Models;

public sealed class AppConfig
{
    public SmtpSettings Smtp { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public List<DriveRule> DriveRules { get; set; } = new();
    public List<AlertRecord> History { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    /// <summary>StartTls, StartTlsWhenAvailable, SslOnConnect, None or Auto.</summary>
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    /// <summary>DPAPI-protected password, base64 encoded.</summary>
    public string ProtectedPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Drive Space Checker";
    public string Recipients { get; set; } = "";

    public IEnumerable<string> RecipientList =>
        Recipients.Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(x => x.Trim())
                  .Where(x => x.Length > 3)
                  .Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public int DefaultThresholdPercent { get; set; } = 10;
    public double DefaultThresholdGb { get; set; } = 0;
    public int CooldownHours { get; set; } = 12;
    public int CheckIntervalMinutes { get; set; } = 15;
    public bool ShowNotifications { get; set; } = true;
    public bool DailyReportEnabled { get; set; } = false;
    public int DailyReportHour { get; set; } = 8;
    public string LastDailyReportDate { get; set; } = "";
    /// <summary>Last alert time per drive (UTC). Used for cooldown.</summary>
    public Dictionary<string, DateTime> LastAlerts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool IncludeHidden { get; set; } = false;
    public string LastScanPath { get; set; } = "";
}

public sealed class DriveRule
{
    public string DriveLetter { get; set; } = "C";
    public bool Enabled { get; set; } = true;
    public int ThresholdPercent { get; set; } = 10;
    public double ThresholdGb { get; set; } = 0;
}

public sealed class AlertRecord
{
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Success { get; set; }
    public string Error { get; set; } = "";
}
