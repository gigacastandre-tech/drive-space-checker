using DriveSpaceChecker.Models;

namespace DriveSpaceChecker.Services;

public sealed class AlertEvent
{
    public required AlertRecord Record { get; init; }
    public required IReadOnlyList<DriveSnapshot> Drives { get; init; }
}

public sealed class CheckResult
{
    public required IReadOnlyList<DriveSnapshot> Drives { get; init; }
    public required IReadOnlyList<DriveSnapshot> Triggered { get; init; }
    public bool EmailSent { get; init; }
    public bool DailyReportSent { get; init; }
    public string? Error { get; init; }
}

public sealed class MonitorService : IDisposable
{
    private readonly ConfigService _config;
    private readonly EmailService _email;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MonitorService(ConfigService config, EmailService email)
    {
        _config = config;
        _email = email;
    }

    public event Action<IReadOnlyList<DriveSnapshot>>? DrivesUpdated;
    public event Action<AlertEvent>? AlertRaised;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => LoopAsync(token), token);
    }

    public void Restart() => Start();

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(false, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error("Automatic drive check failed.", ex);
            }

            var minutes = Math.Clamp(_config.Current.Alerts.CheckIntervalMinutes, 1, 1440);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<CheckResult> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var config = _config.Current;
            var drives = DriveService.GetDrives(config);
            DrivesUpdated?.Invoke(drives);

            var now = DateTime.UtcNow;
            var due = new List<DriveSnapshot>();
            var recovered = new List<string>();

            foreach (var drive in drives)
            {
                var rule = DriveService.GetEffectiveRule(config, drive.Name, out var isOverride);
                drive.Rule = rule;
                drive.IsOverride = isOverride;
                drive.IsTriggered = DriveService.IsTriggered(config, drive, rule);

                if (!drive.IsTriggered)
                {
                    if (config.Alerts.LastAlerts.ContainsKey(drive.Name) && HasRecovered(drive, rule))
                    {
                        recovered.Add(drive.Name);
                    }
                    continue;
                }

                if (config.Alerts.LastAlerts.TryGetValue(drive.Name, out var lastAlert) &&
                    (now - lastAlert).TotalHours < Math.Max(0, config.Alerts.CooldownHours))
                {
                    continue;
                }
                due.Add(drive);
            }

            if (recovered.Count > 0)
            {
                _config.Mutate(c =>
                {
                    foreach (var name in recovered) c.Alerts.LastAlerts.Remove(name);
                });
            }

            var emailSent = false;
            var dailySent = false;
            string? error = null;

            if (due.Count > 0)
            {
                var result = await _email.SendAlertAsync(config, due, drives, cancellationToken).ConfigureAwait(false);
                emailSent = result.Success;
                error = result.Success ? null : result.Error;
                var names = string.Join(", ", due.Select(d => $"{d.Name} ({Format.Percent(d.FreePercent)} free)"));
                var record = new AlertRecord
                {
                    Utc = now,
                    Summary = result.Success
                        ? $"Low space alert emailed: {names}"
                        : $"Low space detected ({names}) but the email could not be sent.",
                    Detail = string.Join(Environment.NewLine,
                        due.Select(d =>
                            $"{d.Name} {d.Label}: {Format.Bytes(d.FreeBytes)} free of {Format.Bytes(d.TotalBytes)} " +
                            $"({Format.Percent(d.FreePercent)})")),
                    Success = result.Success,
                    Error = result.Error
                };
                _config.Mutate(c =>
                {
                    foreach (var drive in due) c.Alerts.LastAlerts[drive.Name] = now;
                    c.History.Insert(0, record);
                    if (c.History.Count > 100) c.History.RemoveRange(100, c.History.Count - 100);
                });
                AlertRaised?.Invoke(new AlertEvent { Record = record, Drives = due });
            }
            else if (config.Alerts.Enabled && config.Alerts.DailyReportEnabled && !manual)
            {
                var local = DateTime.Now;
                var today = local.ToString("yyyy-MM-dd");
                if (local.Hour >= config.Alerts.DailyReportHour && config.Alerts.LastDailyReportDate != today)
                {
                    var report = await _email.SendUsageReportAsync(config, drives,
                        "Scheduled daily drive usage report.", cancellationToken).ConfigureAwait(false);
                    dailySent = report.Success;
                    if (report.Success)
                    {
                        _config.Mutate(c => c.Alerts.LastDailyReportDate = today);
                    }
                    else
                    {
                        error = report.Error;
                        var record = new AlertRecord
                        {
                            Utc = now,
                            Summary = "Daily usage report could not be emailed.",
                            Detail = "",
                            Success = false,
                            Error = report.Error
                        };
                        _config.Mutate(c => c.History.Insert(0, record));
                    }
                }
            }

            return new CheckResult
            {
                Drives = drives,
                Triggered = due,
                EmailSent = emailSent,
                DailyReportSent = dailySent,
                Error = error
            };
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private static bool HasRecovered(DriveSnapshot drive, DriveRule rule)
    {
        var percentOk = rule.ThresholdPercent <= 0 || drive.FreePercent > rule.ThresholdPercent + 2;
        var gbOk = rule.ThresholdGb <= 0 || drive.FreeGb > rule.ThresholdGb + 1;
        return percentOk && gbOk;
    }

    public void Dispose()
    {
        Stop();
        _checkGate.Dispose();
    }
}
