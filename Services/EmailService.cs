using System.Net;
using System.Text;
using DriveSpaceChecker.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DriveSpaceChecker.Services;

public sealed record EmailResult(bool Success, string Error);

public sealed class EmailService
{
    public static List<string> Validate(AppConfig config)
    {
        var errors = new List<string>();
        var smtp = config.Smtp;
        if (string.IsNullOrWhiteSpace(smtp.Host)) errors.Add("SMTP server is missing.");
        if (smtp.Port is < 1 or > 65535) errors.Add("SMTP port is invalid.");
        var from = string.IsNullOrWhiteSpace(smtp.FromAddress) ? smtp.Username : smtp.FromAddress;
        if (string.IsNullOrWhiteSpace(from) || !from.Contains('@')) errors.Add("A valid 'From' email address is required.");
        if (!smtp.RecipientList.Any()) errors.Add("At least one recipient email address is required.");
        foreach (var recipient in smtp.RecipientList)
        {
            if (!MailboxAddress.TryParse(recipient, out _))
            {
                errors.Add($"'{recipient}' is not a valid recipient address.");
            }
        }
        return errors;
    }

    public async Task<EmailResult> SendAlertAsync(AppConfig config, IReadOnlyList<DriveSnapshot> triggered,
        IReadOnlyList<DriveSnapshot> allDrives, CancellationToken cancellationToken = default)
    {
        if (triggered.Count == 0) return new EmailResult(true, "");
        var names = string.Join(", ", triggered.Select(d => d.Name));
        var worst = triggered.OrderBy(d => d.FreePercent).First();
        var subject = triggered.Count == 1
            ? $"Drive space alert: {worst.Name} has {Format.Percent(worst.FreePercent)} free ({Format.Bytes(worst.FreeBytes)})"
            : $"Drive space alert: {names} are running low";
        var note = "One or more drives reached the free-space threshold you configured. " +
                   "Free up some space or adjust the threshold in Drive Space Checker.";
        return await SendAsync(config, subject, BuildHtml("Drive space alert", note, allDrives, true),
            BuildText("Drive space alert", note, allDrives), cancellationToken);
    }

    public async Task<EmailResult> SendUsageReportAsync(AppConfig config, IReadOnlyList<DriveSnapshot> drives,
        string? note = null, CancellationToken cancellationToken = default)
    {
        var subject = $"Drive space report – {Environment.MachineName} – {DateTime.Now:yyyy-MM-dd HH:mm}";
        note ??= "Here is the current space usage on this computer.";
        return await SendAsync(config, subject, BuildHtml("Drive space report", note, drives, false),
            BuildText("Drive space report", note, drives), cancellationToken);
    }

    public async Task<EmailResult> SendTestAsync(AppConfig config, IReadOnlyList<DriveSnapshot> drives,
        CancellationToken cancellationToken = default)
    {
        var subject = $"Drive Space Checker test email – {Environment.MachineName}";
        const string note = "This is a test email. If you received it, email alerts are configured correctly. " +
                            "The current drive usage is listed below.";
        return await SendAsync(config, subject, BuildHtml("Test email", note, drives, false),
            BuildText("Test email", note, drives), cancellationToken);
    }

    private async Task<EmailResult> SendAsync(AppConfig config, string subject, string html, string text,
        CancellationToken cancellationToken)
    {
        var errors = Validate(config);
        if (errors.Count > 0)
        {
            return new EmailResult(false, string.Join(" ", errors));
        }

        var smtp = config.Smtp;
        var fromAddress = string.IsNullOrWhiteSpace(smtp.FromAddress) ? smtp.Username : smtp.FromAddress;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(smtp.FromName) ? "Drive Space Checker" : smtp.FromName,
            fromAddress));
        foreach (var recipient in smtp.RecipientList)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();
        message.Headers.Add("X-DriveSpaceChecker", Environment.MachineName);

        var password = Dpapi.Unprotect(smtp.ProtectedPassword);
        using var client = new SmtpClient { Timeout = 30000 };
        try
        {
            await client.ConnectAsync(smtp.Host.Trim(), smtp.Port, MapSecurity(smtp.Security), cancellationToken);
            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username.Trim(), password, cancellationToken);
            }
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            Log.Info($"Email sent to {string.Join(", ", smtp.RecipientList)}: {subject}");
            return new EmailResult(true, "");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Sending email failed.", ex);
            var hint = ex.Message;
            if (hint.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                hint.Contains("credentials", StringComparison.OrdinalIgnoreCase))
            {
                hint += " Check the username and app password for your email provider.";
            }
            return new EmailResult(false, hint);
        }
    }

    private static SecureSocketOptions MapSecurity(string? security) => security switch
    {
        "SslOnConnect" => SecureSocketOptions.SslOnConnect,
        "StartTlsWhenAvailable" => SecureSocketOptions.StartTlsWhenAvailable,
        "None" => SecureSocketOptions.None,
        "Auto" => SecureSocketOptions.Auto,
        _ => SecureSocketOptions.StartTls
    };

    private static string BuildHtml(string heading, string note, IReadOnlyList<DriveSnapshot> drives, bool alert)
    {
        var sb = new StringBuilder();
        var accent = alert ? "#DC2626" : "#2563EB";
        sb.Append("<!doctype html><html><body style=\"margin:0;padding:24px;background:#f1f5f9;font-family:Segoe UI,Arial,sans-serif;color:#0f172a\">");
        sb.Append("<div style=\"max-width:760px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden\">");
        sb.Append("<div style=\"padding:20px 24px;border-bottom:1px solid #e2e8f0\">");
        sb.Append($"<div style=\"font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:{accent};font-weight:700\">Drive Space Checker</div>");
        sb.Append($"<h1 style=\"margin:6px 0 0;font-size:22px;color:#0f172a\">{WebUtility.HtmlEncode(heading)}</h1>");
        sb.Append($"<div style=\"margin-top:6px;font-size:13px;color:#64748b\">{WebUtility.HtmlEncode(Environment.MachineName)} · {DateTime.Now:dddd, dd MMMM yyyy HH:mm}</div>");
        sb.Append("</div>");
        sb.Append("<div style=\"padding:20px 24px\">");
        sb.Append($"<p style=\"margin:0 0 16px;font-size:14px;line-height:1.5;color:#334155\">{WebUtility.HtmlEncode(note)}</p>");
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;border-collapse:collapse;font-size:13px\">");
        sb.Append("<thead><tr style=\"background:#f8fafc;color:#475569;text-align:left\">");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0\">Drive</th>");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0\">Label</th>");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0;text-align:right\">Total</th>");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0;text-align:right\">Used</th>");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0;text-align:right\">Free</th>");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0;text-align:right\">Free %</th>");
        sb.Append("<th style=\"padding:9px 10px;border-bottom:1px solid #e2e8f0\">Status</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var drive in drives)
        {
            var low = drive.IsTriggered || (alert && drive.FreePercent <= 10);
            var rowBg = low ? "#fff7f7" : "#ffffff";
            var statusColor = low ? "#b91c1c" : "#15803d";
            var statusBg = low ? "#fee2e2" : "#dcfce7";
            var statusText = low ? "Low space" : "OK";
            sb.Append($"<tr style=\"background:{rowBg}\">");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;font-weight:600\">{WebUtility.HtmlEncode(drive.Name)}</td>");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;color:#475569\">{WebUtility.HtmlEncode(drive.Label)}</td>");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;text-align:right\">{Format.Bytes(drive.TotalBytes)}</td>");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;text-align:right\">{Format.Bytes(drive.UsedBytes)}</td>");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;text-align:right;font-weight:600\">{Format.Bytes(drive.FreeBytes)}</td>");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9;text-align:right\">{Format.Percent(drive.FreePercent)}</td>");
            sb.Append($"<td style=\"padding:9px 10px;border-bottom:1px solid #f1f5f9\"><span style=\"display:inline-block;padding:2px 8px;border-radius:999px;background:{statusBg};color:{statusColor};font-size:11px;font-weight:600\">{statusText}</span></td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<p style=\"margin:18px 0 0;font-size:12px;line-height:1.5;color:#64748b\">Tip: open the <strong>Disk Usage</strong> tab in Drive Space Checker to see exactly which folders and files use the most space.</p>");
        sb.Append("</div>");
        sb.Append("<div style=\"padding:14px 24px;background:#f8fafc;border-top:1px solid #e2e8f0;font-size:11px;color:#94a3b8\">Sent automatically by Drive Space Checker on " + WebUtility.HtmlEncode(Environment.MachineName) + ".</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string BuildText(string heading, string note, IReadOnlyList<DriveSnapshot> drives)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading);
        sb.AppendLine(new string('=', heading.Length));
        sb.AppendLine();
        sb.AppendLine($"{Environment.MachineName} - {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine(note);
        sb.AppendLine();
        foreach (var drive in drives)
        {
            sb.AppendLine($"{drive.Name} ({drive.Label})");
            sb.AppendLine($"  Total: {Format.Bytes(drive.TotalBytes)}   Used: {Format.Bytes(drive.UsedBytes)}   " +
                          $"Free: {Format.Bytes(drive.FreeBytes)} ({Format.Percent(drive.FreePercent)})");
            sb.AppendLine();
        }
        sb.AppendLine("Sent by Drive Space Checker.");
        return sb.ToString();
    }
}
