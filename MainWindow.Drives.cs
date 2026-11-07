using System.Windows;
using System.Windows.Media;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;
using WinForms = System.Windows.Forms;

namespace DriveSpaceChecker;

public partial class MainWindow
{
    private void OnDrivesUpdated(IReadOnlyList<DriveSnapshot> drives)
    {
        if (_disposed) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            _lastDrives = drives;
            RenderDriveCards(drives);
            _lastCheckLocal = DateTime.Now;
            UpdateLastCheckText();
        }));
    }

    private void RefreshDrivesOnly()
    {
        try
        {
            var drives = DriveService.GetDrives(_configService.Current);
            _lastDrives = drives;
            RenderDriveCards(drives);
            _lastCheckLocal = DateTime.Now;
            UpdateLastCheckText();
        }
        catch (Exception ex)
        {
            Log.Error("Could not refresh drive list.", ex);
            SetStatus("Could not read the drives: " + ex.Message);
        }
    }

    private void RenderDriveCards(IReadOnlyList<DriveSnapshot> drives)
    {
        var config = _configService.Current;
        _driveCards.Clear();

        foreach (var drive in drives)
        {
            var alertsOn = config.Alerts.Enabled && (drive.Rule?.Enabled ?? true);
            var low = alertsOn && drive.IsTriggered;
            var warn = alertsOn && !low && drive.FreePercent <= 20;

            var statusText = !alertsOn ? "Alerts off" : low ? "Low space" : warn ? "Getting full" : "Healthy";
            var badgeBg = low ? "#FEF2F2" : warn ? "#FFFBEB" : alertsOn ? "#EFF6FF" : "#F1F5F9";
            var badgeFg = low ? "#B91C1C" : warn ? "#B45309" : alertsOn ? "#1D4ED8" : "#475569";
            var statusBg = low ? "#FEE2E2" : warn ? "#FEF3C7" : alertsOn ? "#DCFCE7" : "#F1F5F9";
            var statusFg = low ? "#991B1B" : warn ? "#92400E" : alertsOn ? "#166534" : "#475569";
            var bar = low ? "#DC2626" : warn ? "#F59E0B" : alertsOn ? "#16A34A" : "#94A3B8";

            _driveCards.Add(new DriveCardVm
            {
                Letter = drive.Name,
                Label = drive.Label,
                Subtitle = $"{drive.FileSystem} · {Format.Bytes(drive.TotalBytes)}",
                RootPath = drive.RootPath,
                FreeText = Format.Bytes(drive.FreeBytes),
                UsedText = Format.Bytes(drive.UsedBytes),
                TotalText = Format.Bytes(drive.TotalBytes),
                PercentText = $"{Format.Percent(drive.FreePercent)} free",
                FreeGbText = $"{drive.FreeGb:0.#} GB available",
                RuleText = drive.RuleText,
                StatusText = statusText,
                UsedPercent = Math.Clamp(drive.UsedPercent, 0, 100),
                BadgeBackground = BrushFromHex(badgeBg),
                BadgeForeground = BrushFromHex(badgeFg),
                StatusBackground = BrushFromHex(statusBg),
                StatusForeground = BrushFromHex(statusFg),
                BarBrush = BrushFromHex(bar)
            });
        }

        var total = drives.Sum(d => d.TotalBytes);
        var free = drives.Sum(d => d.FreeBytes);
        var used = Math.Max(0, total - free);
        TotalCapacityText.Text = drives.Count == 0 ? "No drives" : Format.Bytes(total);
        TotalUsedText.Text = Format.Bytes(used);
        TotalFreeText.Text = Format.Bytes(free);
        DriveCountText.Text = $"{drives.Count} drive{(drives.Count == 1 ? "" : "s")} monitored";
        UsedPercentText.Text = total > 0 ? $"{used * 100.0 / total:0.#}% of all drives" : "";
        FreePercentText.Text = total > 0 ? $"{free * 100.0 / total:0.#}% available" : "";

        var lowCount = _driveCards.Count(c => c.StatusText == "Low space");
        DrivesHintText.Text = lowCount > 0
            ? $"{lowCount} drive{(lowCount == 1 ? "" : "s")} below the free-space threshold."
            : "Free space is checked automatically in the background.";
    }

    private void UpdateLastCheckText()
    {
        LastCheckText.Text = _lastCheckLocal?.ToString("HH:mm:ss") ?? "–";
        NextCheckText.Text = _configService.Current.Alerts.Enabled
            ? $"next check within {_configService.Current.Alerts.CheckIntervalMinutes} min"
            : "monitoring paused";
    }

    private void UpdateHeaderStatus()
    {
        var alerts = _configService.Current.Alerts;
        if (alerts.Enabled)
        {
            HeaderStatusText.Text = $"Monitoring active · every {alerts.CheckIntervalMinutes} min";
            MonitoringBadge.Background = (Brush)FindResource("OkSoftBrush");
            MonitoringDot.Fill = (Brush)FindResource("OkBrush");
            HeaderStatusText.Foreground = BrushFromHex("#166534");
        }
        else
        {
            HeaderStatusText.Text = "Monitoring paused";
            MonitoringBadge.Background = BrushFromHex("#F1F5F9");
            MonitoringDot.Fill = BrushFromHex("#94A3B8");
            HeaderStatusText.Foreground = BrushFromHex("#475569");
        }
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        await CheckNowAsync(false);
    }

    private async Task CheckNowAsync(bool fromTray)
    {
        if (_busy)
        {
            SetStatus("A drive check is already running…");
            return;
        }
        _busy = true;
        SetStatus("Checking drive space…");
        try
        {
            var result = await _monitor.CheckAsync(true);
            _lastDrives = result.Drives;
            RenderDriveCards(result.Drives);
            _lastCheckLocal = DateTime.Now;
            UpdateLastCheckText();

            var low = result.Drives.Where(d => d.IsTriggered).ToList();
            string message;
            if (result.EmailSent)
            {
                message = "Check complete — low-space alert email sent.";
            }
            else if (low.Count > 0 && !string.IsNullOrWhiteSpace(result.Error))
            {
                message = "Check complete — low space found, but the alert email failed: " + result.Error;
            }
            else if (low.Count > 0)
            {
                message = $"Check complete — {low.Count} drive(s) low. An alert was already sent within the cooldown window.";
            }
            else if (!_configService.Current.Alerts.Enabled)
            {
                message = "Check complete. Automatic monitoring is turned off.";
            }
            else
            {
                message = "Check complete — all drives are within their thresholds.";
            }
            SetStatus(message);

            if (fromTray && !IsVisible)
            {
                ShowBalloon("Drive check complete", message, low.Count > 0);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Manual drive check failed.", ex);
            SetStatus("Drive check failed: " + ex.Message);
            if (!IsVisible) ShowBalloon("Drive check failed", ex.Message, true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnAlertRaised(AlertEvent alert)
    {
        if (_disposed) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            _history.Insert(0, new AlertRecordVm(alert.Record));
            while (_history.Count > 50) _history.RemoveAt(_history.Count - 1);
            UpdateHistoryEmptyState();
            SetStatus(alert.Record.Summary);
            if (_configService.Current.Alerts.ShowNotifications)
            {
                ShowBalloon("Drive space alert", alert.Record.Summary, !alert.Record.Success);
            }
            RefreshDrivesOnly();
        }));
    }

    private void ShowBalloon(string title, string text, bool warning)
    {
        try
        {
            _tray?.ShowBalloonTip(8000, title, text,
                warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // ignored
        }
    }

    private void ScanDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not DriveCardVm card) return;
        MainTabs.SelectedIndex = 2;
        ScanPathBox.Text = card.RootPath;
        _ = StartScanAsync(card.RootPath);
    }

    private void OpenDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is DriveCardVm card)
        {
            OpenInExplorer(card.RootPath, false);
        }
    }

    private void GoToAlerts_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 1;
    }
}
