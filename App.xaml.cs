using System.Windows;
using System.Windows.Threading;
using DriveSpaceChecker.Services;

namespace DriveSpaceChecker;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureDirectories();

        if (e.Args.Any(arg => arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var exitCode = SelfTest.Run(e.Args);
            Shutdown(exitCode);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var createdNew = false;
        _singleInstance = new Mutex(true, @"Local\DriveSpaceChecker.SingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Drive Space Checker is already running.\n\nLook for its icon in the notification area (next to the clock).",
                "Drive Space Checker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var startHidden = e.Args.Any(arg => arg.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow();
        MainWindow = window;
        if (!startHidden)
        {
            window.Show();
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            "Drive Space Checker ran into a problem:\n\n" + e.Exception.Message +
            "\n\nThe details were written to the log file:\n" + AppPaths.LogFile,
            "Drive Space Checker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        }
        catch
        {
            // ignored
        }
        base.OnExit(e);
    }
}
