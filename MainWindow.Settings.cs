using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;

namespace DriveSpaceChecker;

public partial class MainWindow
{
    private sealed record SecurityOption(string Value, string Label);

    private static readonly SecurityOption[] SecurityOptions =
    {
        new("StartTls", "STARTTLS (recommended, usually port 587)"),
        new("SslOnConnect", "SSL / TLS (usually port 465)"),
        new("StartTlsWhenAvailable", "STARTTLS if available"),
        new("Auto", "Auto"),
        new("None", "None (unencrypted)")
    };

    private void LoadSettingsIntoUi()
    {
        var config = _configService.Current;
        var alerts = config.Alerts;

        AlertEnabledCheck.IsChecked = alerts.Enabled;
        IntervalBox.Text = alerts.CheckIntervalMinutes.ToString(CultureInfo.CurrentCulture);
        CooldownBox.Text = alerts.CooldownHours.ToString(CultureInfo.CurrentCulture);
        NotifyCheck.IsChecked = alerts.ShowNotifications;
        MinimizeToTrayCheck.IsChecked = config.General.MinimizeToTray;
        StartWithWindowsCheck.IsChecked = config.General.StartWithWindows || StartupService.IsEnabled();
        DefaultPercentBox.Text = alerts.DefaultThresholdPercent.ToString(CultureInfo.CurrentCulture);
        DefaultGbBox.Text = alerts.DefaultThresholdGb.ToString("0.#", CultureInfo.CurrentCulture);
        DailyReportCheck.IsChecked = alerts.DailyReportEnabled;

        DailyHourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
        DailyHourCombo.SelectedIndex = Math.Clamp(alerts.DailyReportHour, 0, 23);

        SecurityCombo.ItemsSource = SecurityOptions;
        SecurityCombo.SelectedValue = string.IsNullOrWhiteSpace(config.Smtp.Security) ? "StartTls" : config.Smtp.Security;
        if (SecurityCombo.SelectedIndex < 0) SecurityCombo.SelectedValue = "StartTls";

        HostBox.Text = config.Smtp.Host;
        PortBox.Text = config.Smtp.Port.ToString(CultureInfo.CurrentCulture);
        UserBox.Text = config.Smtp.Username;
        PasswordBox.Password = Dpapi.Unprotect(config.Smtp.ProtectedPassword);
        FromNameBox.Text = config.Smtp.FromName;
        FromAddressBox.Text = config.Smtp.FromAddress;
        RecipientsBox.Text = config.Smtp.Recipients;

        IncludeHiddenCheck.IsChecked = config.General.IncludeHidden;
        var roots = GetScanRoots();
        ScanPathBox.ItemsSource = roots;
        var last = config.General.LastScanPath;
        ScanPathBox.Text = !string.IsNullOrWhiteSpace(last) && Directory.Exists(last)
            ? last
            : roots.FirstOrDefault() ?? "C:\\";

        _overrides.Clear();
        foreach (var rule in config.DriveRules)
        {
            _overrides.Add(new DriveRuleVm
            {
                DriveLetter = ConfigService.NormalizeLetter(rule.DriveLetter),
                Enabled = rule.Enabled,
                ThresholdPercent = rule.ThresholdPercent,
                ThresholdGb = rule.ThresholdGb
            });
        }

        _history.Clear();
        foreach (var record in config.History.OrderByDescending(h => h.Utc).Take(50))
        {
            _history.Add(new AlertRecordVm(record));
        }
    }

    private static List<string> GetScanRoots()
    {
        var roots = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;
                    roots.Add(drive.RootDirectory.FullName);
                }
                catch
                {
                    // ignore a single unreadable drive
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not list scan roots: " + ex.Message);
        }
        return roots;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OverridesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            OverridesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ApplyUiToConfig(_configService.Current);
            _configService.Save();
            StartupService.SetEnabled(_configService.Current.General.StartWithWindows);
            _monitor.Restart();
            UpdateHeaderStatus();
            UpdateLastCheckText();
            RefreshDrivesOnly();
            SetStatus("Settings saved. Monitoring restarted with the new schedule.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.", ex);
            ShowError("Settings", "The settings could not be saved: " + ex.Message);
        }
    }

    private void ApplyUiToConfig(AppConfig config)
    {
        var alerts = config.Alerts;
        alerts.Enabled = AlertEnabledCheck.IsChecked == true;
        alerts.CheckIntervalMinutes = ParseInt(IntervalBox.Text, 15, 1, 1440);
        alerts.CooldownHours = ParseInt(CooldownBox.Text, 12, 0, 720);
        alerts.ShowNotifications = NotifyCheck.IsChecked == true;
        alerts.DailyReportEnabled = DailyReportCheck.IsChecked == true;
        alerts.DailyReportHour = DailyHourCombo.SelectedIndex >= 0 ? DailyHourCombo.SelectedIndex : 8;
        alerts.DefaultThresholdPercent = ParseInt(DefaultPercentBox.Text, 10, 0, 100);
        alerts.DefaultThresholdGb = ParseDouble(DefaultGbBox.Text, 0, 0, 100000);

        config.General.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        config.General.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        config.General.IncludeHidden = IncludeHiddenCheck.IsChecked == true;
        config.Smtp = ReadSmtpFromUi();

        config.DriveRules = _overrides
            .Select(rule => new DriveRule
            {
                DriveLetter = ConfigService.NormalizeLetter(rule.DriveLetter),
                Enabled = rule.Enabled,
                ThresholdPercent = Math.Clamp(rule.ThresholdPercent, 0, 100),
                ThresholdGb = Math.Max(0, rule.ThresholdGb)
            })
            .GroupBy(rule => rule.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        // Reflect normalized values back into the form.
        IntervalBox.Text = alerts.CheckIntervalMinutes.ToString(CultureInfo.CurrentCulture);
        CooldownBox.Text = alerts.CooldownHours.ToString(CultureInfo.CurrentCulture);
        DefaultPercentBox.Text = alerts.DefaultThresholdPercent.ToString(CultureInfo.CurrentCulture);
        DefaultGbBox.Text = alerts.DefaultThresholdGb.ToString("0.#", CultureInfo.CurrentCulture);
        PortBox.Text = config.Smtp.Port.ToString(CultureInfo.CurrentCulture);
    }

    private SmtpSettings ReadSmtpFromUi()
    {
        return new SmtpSettings
        {
            Host = HostBox.Text.Trim(),
            Port = ParseInt(PortBox.Text, 587, 1, 65535),
            Security = SecurityCombo.SelectedValue as string ?? "StartTls",
            Username = UserBox.Text.Trim(),
            FromAddress = FromAddressBox.Text.Trim(),
            FromName = FromNameBox.Text.Trim(),
            Recipients = RecipientsBox.Text.Trim(),
            ProtectedPassword = Dpapi.Protect(PasswordBox.Password)
        };
    }

    private static int ParseInt(string? text, int fallback, int min, int max)
    {
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private static double ParseDouble(string? text, double fallback, double min, double max)
    {
        return double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private async void SendTestEmail_Click(object sender, RoutedEventArgs e)
    {
        var temp = new AppConfig { Smtp = ReadSmtpFromUi() };
        var errors = EmailService.Validate(temp);
        if (errors.Count > 0)
        {
            ShowError("Email settings", string.Join(Environment.NewLine, errors));
            return;
        }

        SetEmailBusy(true);
        SetStatus("Sending test email…");
        try
        {
            if (_lastDrives.Count == 0) _lastDrives = DriveService.GetDrives(_configService.Current);
            var result = await _emailService.SendTestAsync(temp, _lastDrives);
            if (result.Success)
            {
                SetStatus("Test email sent successfully.");
                MessageBox.Show(this,
                    "Test email sent to:" + Environment.NewLine + string.Join(Environment.NewLine, temp.Smtp.RecipientList),
                    "Email sent", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                SetStatus("Test email failed: " + result.Error);
                ShowError("Email failed", result.Error);
            }
        }
        finally
        {
            SetEmailBusy(false);
        }
    }

    private async void SendUsageEmail_Click(object sender, RoutedEventArgs e)
    {
        var temp = new AppConfig { Smtp = ReadSmtpFromUi() };
        var errors = EmailService.Validate(temp);
        if (errors.Count > 0)
        {
            ShowError("Email settings", string.Join(Environment.NewLine, errors));
            return;
        }

        SetEmailBusy(true);
        SetStatus("Sending usage report…");
        try
        {
            var drives = DriveService.GetDrives(_configService.Current);
            _lastDrives = drives;
            var result = await _emailService.SendUsageReportAsync(temp, drives,
                "Usage report requested from Drive Space Checker.");
            if (result.Success)
            {
                SetStatus("Usage report emailed.");
                MessageBox.Show(this, "The usage report was emailed to:" + Environment.NewLine +
                                      string.Join(Environment.NewLine, temp.Smtp.RecipientList),
                    "Usage report sent", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                SetStatus("Usage report failed: " + result.Error);
                ShowError("Email failed", result.Error);
            }
        }
        finally
        {
            SetEmailBusy(false);
        }
    }

    private async Task SendUsageReportAsync()
    {
        var config = _configService.Current;
        var errors = EmailService.Validate(config);
        if (errors.Count > 0)
        {
            var message = "Email settings are incomplete: " + string.Join(" ", errors);
            if (IsVisible) ShowError("Email settings", message);
            else ShowBalloon("Email not configured", message, true);
            return;
        }

        SetStatus("Sending usage report…");
        try
        {
            var drives = DriveService.GetDrives(config);
            _lastDrives = drives;
            var result = await _emailService.SendUsageReportAsync(config, drives);
            if (result.Success)
            {
                SetStatus("Usage report emailed.");
                if (!IsVisible) ShowBalloon("Usage report sent", "The drive usage report was emailed.", false);
            }
            else
            {
                SetStatus("Usage report failed: " + result.Error);
                if (!IsVisible) ShowBalloon("Usage report failed", result.Error, true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not send usage report.", ex);
            SetStatus("Usage report failed: " + ex.Message);
        }
    }

    private void SetEmailBusy(bool busy)
    {
        TestEmailButton.IsEnabled = !busy;
        UsageEmailButton.IsEnabled = !busy;
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var used = _overrides.Select(r => r.DriveLetter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var letter = "CDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()).FirstOrDefault(l => !used.Contains(l)) ?? "C";
        var rule = new DriveRuleVm
        {
            DriveLetter = letter,
            Enabled = true,
            ThresholdPercent = _configService.Current.Alerts.DefaultThresholdPercent,
            ThresholdGb = _configService.Current.Alerts.DefaultThresholdGb
        };
        _overrides.Add(rule);
        OverridesGrid.SelectedItem = rule;
        OverridesGrid.ScrollIntoView(rule);
        SetStatus("New per-drive rule added. Set the values and press Save settings.");
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (OverridesGrid.SelectedItem is DriveRuleVm rule)
        {
            _overrides.Remove(rule);
            SetStatus("Rule removed. Press Save settings to apply it.");
        }
        else
        {
            SetStatus("Select a per-drive rule first, then press Remove.");
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _configService.Current.History.Clear();
        _configService.Save();
        _history.Clear();
        UpdateHistoryEmptyState();
        SetStatus("Alert history cleared.");
    }

    private void UpdateHistoryEmptyState()
    {
        HistoryEmptyText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
