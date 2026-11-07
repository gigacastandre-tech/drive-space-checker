using System.IO;
using System.Text;
using System.Windows;
using DriveSpaceChecker.Controls;
using DriveSpaceChecker.Models;

namespace DriveSpaceChecker.Services;

public static class SelfTest
{
    public static int Run(string[] args)
    {
        var outputPath = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : Path.Combine(Path.GetTempPath(), "dsc-selftest.txt");

        var lines = new List<string>();
        var failures = 0;

        void Check(string name, Func<bool> test)
        {
            try
            {
                var ok = test();
                lines.Add($"[{(ok ? "PASS" : "FAIL")}] {name}");
                if (!ok) failures++;
            }
            catch (Exception ex)
            {
                lines.Add($"[FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
                failures++;
            }
        }

        var dataDir = Path.Combine(Path.GetTempPath(), "dsc-selftest-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("DSC_DATA_DIR", dataDir);
        var scanDir = Path.Combine(Path.GetTempPath(), "dsc-scan-" + Guid.NewGuid().ToString("N"));
        var scanSub = Path.Combine(scanDir, "sub");

        try
        {
            Directory.CreateDirectory(scanSub);
            File.WriteAllBytes(Path.Combine(scanDir, "a.bin"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(scanDir, "b.txt"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(scanSub, "c.bin"), new byte[4096]);

            lines.Add("Drive Space Checker self-test");
            lines.Add($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"Machine: {Environment.MachineName}");
            lines.Add($"Runtime: {Environment.Version}");
            lines.Add("");

            Check("Data folders can be created", () =>
            {
                AppPaths.EnsureDirectories();
                return Directory.Exists(AppPaths.DataDir) && Directory.Exists(Path.GetDirectoryName(AppPaths.LogFile)!);
            });

            Check("DPAPI encrypts and decrypts a password", () =>
            {
                const string secret = "p@ssw0rd-with-ümlaut";
                var protectedValue = Dpapi.Protect(secret);
                return protectedValue.Length > 20 && protectedValue != secret && Dpapi.Unprotect(protectedValue) == secret;
            });

            Check("Settings round-trip through JSON", () =>
            {
                var service = new ConfigService();
                service.Current.Smtp.Host = "smtp.example.com";
                service.Current.Smtp.Port = 587;
                service.Current.Smtp.Recipients = "one@example.com; two@example.com";
                service.Current.Alerts.DefaultThresholdPercent = 7;
                service.Current.Alerts.CheckIntervalMinutes = 5;
                service.Current.DriveRules.Add(new DriveRule { DriveLetter = "D", ThresholdPercent = 4, ThresholdGb = 12 });
                service.Save();

                var reloaded = new ConfigService();
                return reloaded.Current.Smtp.Host == "smtp.example.com" &&
                       reloaded.Current.Smtp.RecipientList.Count() == 2 &&
                       reloaded.Current.Alerts.DefaultThresholdPercent == 7 &&
                       reloaded.Current.Alerts.CheckIntervalMinutes == 5 &&
                       reloaded.Current.DriveRules.Count == 1 &&
                       reloaded.Current.DriveRules[0].ThresholdGb == 12;
            });

            Check("Drive letters are normalized", () =>
                ConfigService.NormalizeLetter("c:") == "C" &&
                ConfigService.NormalizeLetter("D:\\") == "D");

            Check("Fixed drives can be enumerated", () =>
            {
                var drives = DriveService.GetDrives(new AppConfig());
                lines.Add("       Drives: " + string.Join(", ",
                    drives.Select(d => $"{d.Name} {Format.Bytes(d.FreeBytes)} free ({Format.Percent(d.FreePercent)})")));
                return drives.Count > 0;
            });

            ScanResult? scan = null;
            Check("Folder scan computes sizes and totals", () =>
            {
                var scanner = new DiskScanner();
                scan = scanner.ScanAsync(scanDir, false, null, CancellationToken.None).GetAwaiter().GetResult();
                lines.Add($"       Scan: {scan.TotalFiles} files, {scan.TotalFolders} folders, {scan.TotalBytes} bytes");
                return scan.TotalFiles == 3 &&
                       scan.TotalFolders == 1 &&
                       scan.TotalBytes == 7168 &&
                       scan.Root.Size == 7168 &&
                       scan.Root.Children is { Count: 3 } &&
                       scan.Root.Children[0].Name == "sub" &&
                       scan.LargestFiles.Count == 3;
            });

            Check("File-type grouping works", () =>
                scan != null &&
                scan.Extensions.TryGetValue("bin", out var binBytes) && binBytes == 5120 &&
                scan.Extensions.TryGetValue("txt", out var txtBytes) && txtBytes == 2048);

            Check("Treemap layout fills the canvas without overlaps", () =>
            {
                if (scan == null) return false;
                var items = TreemapLayout.BuildItems(scan.Root, scan);
                var slices = TreemapLayout.Layout(items, new Rect(0, 0, 800, 600));
                if (slices.Count != items.Count) return false;
                var totalArea = slices.Sum(s => s.Rect.Width * s.Rect.Height);
                if (Math.Abs(totalArea - 800 * 600) > 800 * 600 * 0.02) return false;
                foreach (var slice in slices)
                {
                    if (slice.Rect.X < -0.5 || slice.Rect.Y < -0.5 ||
                        slice.Rect.Right > 800.5 || slice.Rect.Bottom > 600.5) return false;
                }
                for (var i = 0; i < slices.Count; i++)
                {
                    for (var j = i + 1; j < slices.Count; j++)
                    {
                        var a = slices[i].Rect;
                        var b = slices[j].Rect;
                        var overlap = Rect.Intersect(a, b);
                        if (overlap.Width > 0.5 && overlap.Height > 0.5) return false;
                    }
                }
                return true;
            });

            Check("Deleting a node adjusts ancestor totals", () =>
            {
                if (scan == null) return false;
                var before = scan.Root.Size;
                var child = scan.Root.Children!.First(c => c.Name == "sub");
                child.RemoveFromParent();
                return scan.Root.Size == before - 4096 && scan.Root.FileCount == 2;
            });

            Check("Email settings validation catches missing fields", () =>
                EmailService.Validate(new AppConfig()).Count >= 3);

            Check("Email settings validation accepts a complete configuration", () =>
            {
                var config = new AppConfig
                {
                    Smtp = new SmtpSettings
                    {
                        Host = "smtp.example.com",
                        Port = 587,
                        FromAddress = "sender@example.com",
                        Recipients = "recipient@example.com"
                    }
                };
                return EmailService.Validate(config).Count == 0;
            });

            Check("Startup registry helper is callable", () =>
            {
                var current = StartupService.IsEnabled();
                return current || !current; // read-only check
            });

            lines.Add("");
            lines.Add(failures == 0 ? "RESULT: ALL TESTS PASSED" : $"RESULT: {failures} TEST(S) FAILED");
        }
        catch (Exception ex)
        {
            lines.Add("[FAIL] Self-test crashed: " + ex);
            failures++;
        }
        finally
        {
            try
            {
                if (Directory.Exists(scanDir)) Directory.Delete(scanDir, true);
                if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
            }
            catch
            {
                // ignored
            }
        }

        try
        {
            File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }
        catch
        {
            // ignored
        }

        return failures == 0 ? 0 : 1;
    }
}
