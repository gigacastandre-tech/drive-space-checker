using System.IO;

namespace DriveSpaceChecker.Services;

public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("DSC_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DriveSpaceChecker");
        }
    }

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string LogFile => Path.Combine(DataDir, "logs", "app.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    }
}
