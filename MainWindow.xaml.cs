using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DriveSpaceChecker.Controls;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DriveSpaceChecker;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly EmailService _emailService;
    private readonly MonitorService _monitor;
    private readonly DiskScanner _scanner = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly ObservableCollection<DriveCardVm> _driveCards = new();
    private readonly ObservableCollection<DriveRuleVm> _overrides = new();
    private readonly ObservableCollection<AlertRecordVm> _history = new();
    private readonly ObservableCollection<ContentRow> _contents = new();
    private readonly ObservableCollection<ContentRow> _largestFiles = new();
    private readonly ObservableCollection<ContentRow> _largestFolders = new();
    private readonly ObservableCollection<TypeRow> _types = new();

    private WinForms.NotifyIcon? _tray;
    private Drawing.Icon? _trayIcon;
    private IReadOnlyList<DriveSnapshot> _lastDrives = Array.Empty<DriveSnapshot>();
    private DateTime? _lastCheckLocal;
    private CancellationTokenSource? _scanCts;
    private ScanResult? _scanResult;
    private FsNode? _currentNode;
    private bool _exiting;
    private bool _disposed;
    private bool _balloonShown;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _configService = new ConfigService();
        _emailService = new EmailService();
        _monitor = new MonitorService(_configService, _emailService);

        DriveItems.ItemsSource = _driveCards;
        OverridesGrid.ItemsSource = _overrides;
        HistoryItems.ItemsSource = _history;
        ContentsGrid.ItemsSource = _contents;
        LargestFilesGrid.ItemsSource = _largestFiles;
        LargestFoldersGrid.ItemsSource = _largestFolders;
        TypesItems.ItemsSource = _types;
        OverrideDriveColumn.ItemsSource = "CDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()).ToList();

        LoadSettingsIntoUi();
        Loaded += (_, _) => ClearScanPathSelection();
        WireScanEvents();
        CreateTrayIcon();

        _monitor.DrivesUpdated += OnDrivesUpdated;
        _monitor.AlertRaised += OnAlertRaised;
        _monitor.Start();

        RefreshDrivesOnly();
        UpdateHeaderStatus();
        UpdateHistoryEmptyState();
        UpdateScanStatus();

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _uiTimer.Tick += (_, _) => RefreshDrivesOnly();
        _uiTimer.Start();

        Closing += OnWindowClosing;
    }

    // ---------------------------------------------------------------- shared helpers

    private static Brush BrushFromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void ShowError(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _configService.Current.General.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_balloonShown && _tray != null)
            {
                _balloonShown = true;
                _tray.ShowBalloonTip(4000, "Drive Space Checker is still running",
                    "It will keep checking your drives and email you when space runs low. Use the tray icon to open it again.",
                    WinForms.ToolTipIcon.Info);
            }
            return;
        }

        DisposeResources();
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            _configService.Save();
        }
        catch
        {
            // ignored
        }
        DisposeResources();
        Close();
        Application.Current.Shutdown();
    }

    private void DisposeResources()
    {
        if (_disposed) return;
        _disposed = true;
        _uiTimer.Stop();
        try
        {
            _scanCts?.Cancel();
        }
        catch
        {
            // ignored
        }
        _monitor.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void CreateTrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DriveSpaceChecker.exe");
            if (File.Exists(exe)) _trayIcon = Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // ignored
        }
        _trayIcon ??= Drawing.SystemIcons.Application;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Drive Space Checker", null,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)));
        menu.Items.Add("Check drive space now", null,
            (_, _) => Dispatcher.BeginInvoke(new Action(async () => await CheckNowAsync(true))));
        menu.Items.Add("Send usage email", null,
            (_, _) => Dispatcher.BeginInvoke(new Action(async () => await SendUsageReportAsync())));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication)));

        _tray = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Drive Space Checker",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
    }
}
