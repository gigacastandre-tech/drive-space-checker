using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DriveSpaceChecker.Controls;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;

namespace DriveSpaceChecker;

public partial class MainWindow
{
    private bool _scanning;

    private void ClearScanPathSelection()
    {
        if (ScanPathBox.Template?.FindName("PART_EditableTextBox", ScanPathBox) is TextBox textBox)
        {
            textBox.Select(textBox.Text.Length, 0);
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private void WireScanEvents()
    {
        Treemap.NodeActivated += node =>
        {
            if (node.IsDirectory)
            {
                _currentNode = node;
                RefreshCurrentView();
            }
            else
            {
                OpenInExplorer(node.FullPath, true);
            }
        };
        Treemap.NodeSelected += node =>
        {
            if (node != null) SetStatus($"{node.FullPath}  ·  {Format.Bytes(node.Size)}");
        };
        Treemap.ContextRequested += ShowTreemapContextMenu;
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        await StartScanAsync(ScanPathBox.Text);
    }

    private async Task StartScanAsync(string? requestedPath)
    {
        if (_scanning)
        {
            SetStatus("A scan is already running. Press Cancel first.");
            return;
        }

        var path = NormalizeScanPath(requestedPath);
        if (path == null)
        {
            ShowError("Scan location", "Choose a drive or folder that exists, then press Scan.");
            return;
        }

        _scanning = true;
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        ScanButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        ScanProgressPanel.Visibility = Visibility.Visible;
        ScanProgressText.Text = "Preparing scan…";
        SetStatus("Scanning " + path + "…");

        var progress = new Progress<ScanProgress>(p =>
        {
            if (!_scanning) return;
            ScanProgressText.Text = $"{Format.Count(p.Files)} files · {Format.Bytes(p.Bytes)} · {p.CurrentPath}";
        });

        try
        {
            var result = await _scanner.ScanAsync(path, IncludeHiddenCheck.IsChecked == true, progress, token);
            if (token.IsCancellationRequested) return;

            _scanResult = result;
            _currentNode = result.Root;
            ScanPathBox.Text = result.RootPath;
            _configService.Current.General.LastScanPath = result.RootPath;
            _configService.Current.General.IncludeHidden = IncludeHiddenCheck.IsChecked == true;
            _configService.Save();

            DetailsTabs.SelectedIndex = 0;
            RefreshCurrentView();
            PopulateLargest();
            PopulateTypes();
            SetStatus($"Scan complete — {Format.Count(result.Root.FileCount)} files, " +
                      $"{Format.Count(result.Root.FolderCount)} folders, {Format.Bytes(result.Root.Size)} " +
                      $"in {result.Elapsed.TotalSeconds:0.0} s.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Scan cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error("Scan failed.", ex);
            ShowError("Scan failed", ex.Message);
            SetStatus("Scan failed: " + ex.Message);
        }
        finally
        {
            _scanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
            ScanButton.IsEnabled = true;
            CancelScanButton.IsEnabled = false;
            ScanProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string? NormalizeScanPath(string? path)
    {
        var text = path?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            if (text.Length == 2 && text[1] == ':') text += "\\";
            var full = Path.GetFullPath(text);
            return Directory.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_scanning) return;
        try
        {
            _scanCts?.Cancel();
            SetStatus("Cancelling scan…");
        }
        catch
        {
            // ignored
        }
    }

    private void BrowseScanFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to scan" };
        var current = NormalizeScanPath(ScanPathBox.Text);
        if (current != null) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true)
        {
            ScanPathBox.Text = dialog.FolderName;
            SetStatus("Press Scan to analyse this folder.");
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode?.Parent == null) return;
        _currentNode = _currentNode.Parent;
        RefreshCurrentView();
    }

    private void OpenCurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode != null) OpenInExplorer(_currentNode.FullPath, false);
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        var path = _scanResult?.RootPath ?? ScanPathBox.Text;
        await StartScanAsync(path);
    }

    private void RefreshCurrentView()
    {
        if (_scanResult == null)
        {
            Treemap.Clear();
            _contents.Clear();
            BreadcrumbText.Text = "No folder selected";
            FolderSummaryText.Text = "";
            UpButton.IsEnabled = false;
            return;
        }

        _currentNode ??= _scanResult.Root;
        Treemap.SetData(_currentNode, _scanResult);
        PopulateContents();
        UpdateBreadcrumb();
        UpdateScanStatus();
    }

    private void PopulateContents()
    {
        _contents.Clear();
        if (_currentNode?.Children == null) return;
        foreach (var child in _currentNode.Children.OrderByDescending(c => c.Size).Take(5000))
        {
            _contents.Add(ContentRow.From(child));
        }
    }

    private void PopulateLargest()
    {
        _largestFiles.Clear();
        _largestFolders.Clear();
        if (_scanResult == null) return;
        foreach (var node in _scanResult.LargestFiles.Take(250))
        {
            _largestFiles.Add(ContentRow.From(node));
        }
        foreach (var node in _scanResult.LargestFolders.Where(n => n.Parent != null).Take(250))
        {
            _largestFolders.Add(ContentRow.From(node));
        }
    }

    private void PopulateTypes()
    {
        _types.Clear();
        if (_scanResult == null || _scanResult.Root.Size <= 0) return;
        var total = _scanResult.Root.Size;
        foreach (var pair in _scanResult.Extensions.OrderByDescending(kv => kv.Value).Take(40))
        {
            var category = FileCategories.GetCategory(pair.Key == "(no extension)" ? "" : pair.Key);
            _types.Add(new TypeRow
            {
                Name = pair.Key == "(no extension)" ? "(no extension)" : "." + pair.Key,
                SizeText = Format.Bytes(pair.Value),
                PercentText = Format.Percent(pair.Value * 100.0 / total),
                CategoryBrush = FileCategories.BrushFor(category)
            });
        }
    }

    private void UpdateBreadcrumb()
    {
        if (_currentNode == null)
        {
            BreadcrumbText.Text = "No folder selected";
            FolderSummaryText.Text = "";
            UpButton.IsEnabled = false;
            return;
        }

        BreadcrumbText.Text = _currentNode.FullPath;
        FolderSummaryText.Text = $"{Format.Bytes(_currentNode.Size)} · {Format.Count(_currentNode.FileCount)} files";
        UpButton.IsEnabled = _currentNode.Parent != null;
    }

    private void UpdateScanStatus()
    {
        if (_scanResult == null)
        {
            ScanStatusText.Text = "No scan yet. Choose a drive or folder and press Scan.";
            ScanLegendText.Text = "";
            return;
        }

        var root = _scanResult.Root;
        var inaccessible = _scanResult.Inaccessible > 0
            ? $" · {Format.Count(_scanResult.Inaccessible)} inaccessible"
            : "";
        ScanStatusText.Text = $"{Format.Count(root.FileCount)} files · {Format.Count(root.FolderCount)} folders · " +
                              $"{Format.Bytes(root.Size)} scanned in {_scanResult.Elapsed.TotalSeconds:0.0} s{inaccessible}";
        ScanLegendText.Text = _scanResult.IsDriveRoot
            ? $"{Format.Bytes(_scanResult.DriveFreeBytes)} free on {Path.GetPathRoot(_scanResult.RootPath)} at scan time"
            : "Scanning a folder";
    }

    private void ContentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContentsGrid.SelectedItem is not ContentRow row || row.Node == null) return;
        if (row.IsDirectory)
        {
            _currentNode = row.Node;
            RefreshCurrentView();
        }
        else
        {
            OpenInExplorer(row.FullPath, true);
        }
    }

    private void LargestFilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LargestFilesGrid.SelectedItem is ContentRow row) OpenInExplorer(row.FullPath, true);
    }

    private void LargestFoldersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LargestFoldersGrid.SelectedItem is not ContentRow row || row.Node == null) return;
        _currentNode = row.Node;
        DetailsTabs.SelectedIndex = 0;
        RefreshCurrentView();
    }

    private void OpenSelectedInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRowFromMenu(sender);
        if (row != null) OpenInExplorer(row.FullPath, !row.IsDirectory);
    }

    private void CopySelectedPath_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRowFromMenu(sender);
        if (row == null) return;
        try
        {
            Clipboard.SetText(row.FullPath);
            SetStatus("Path copied: " + row.FullPath);
        }
        catch (Exception ex)
        {
            SetStatus("Could not copy the path: " + ex.Message);
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var row = GetRowFromMenu(sender);
        if (row?.Node != null) DeleteNode(row.Node);
    }

    private static ContentRow? GetRowFromMenu(object sender)
    {
        if (sender is MenuItem item && item.Parent is ContextMenu menu && menu.PlacementTarget is DataGrid grid)
        {
            return grid.SelectedItem as ContentRow;
        }
        return null;
    }

    private void ShowTreemapContextMenu(FsNode node, Point screenPoint)
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = screenPoint.X,
            VerticalOffset = screenPoint.Y
        };

        var open = new MenuItem { Header = "Open in Explorer" };
        open.Click += (_, _) => OpenInExplorer(node.FullPath, !node.IsDirectory);
        var copy = new MenuItem { Header = "Copy full path" };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(node.FullPath);
                SetStatus("Path copied: " + node.FullPath);
            }
            catch
            {
                // ignored
            }
        };
        var delete = new MenuItem { Header = "Delete to Recycle Bin…" };
        delete.Click += (_, _) => DeleteNode(node);

        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void DeleteNode(FsNode node)
    {
        if (node.Parent == null)
        {
            ShowError("Delete", "The scan root itself cannot be deleted from here.");
            return;
        }

        var path = node.FullPath;
        var size = node.Size;
        var kind = node.IsDirectory ? "folder" : "file";
        var answer = MessageBox.Show(this,
            $"Move this {kind} to the Recycle Bin?\n\n{path}\n\nSize: {Format.Bytes(size)}",
            "Delete to Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (!RecycleBin.Delete(path))
        {
            ShowError("Delete failed",
                "Windows could not move the item to the Recycle Bin. It may be in use, or you may not have permission.");
            return;
        }

        var parent = node.Parent;
        var wasInsideCurrent = _currentNode != null && IsPathInside(_currentNode.FullPath, path);
        node.RemoveFromParent();

        foreach (var list in new[] { _largestFiles, _largestFolders })
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (IsPathInside(list[i].FullPath, path)) list.RemoveAt(i);
            }
        }

        if (wasInsideCurrent) _currentNode = parent;
        RefreshCurrentView();
        PopulateTypes();
        SetStatus($"Moved to Recycle Bin: {path} ({Format.Bytes(size)}).");
    }

    private static bool IsPathInside(string candidate, string parent)
    {
        if (candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = parent.TrimEnd('\\') + "\\";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenInExplorer(string path, bool selectFile)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                SetStatus("This item no longer exists: " + path);
                return;
            }
            var arguments = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("Could not open Explorer: " + ex.Message);
        }
    }
}
