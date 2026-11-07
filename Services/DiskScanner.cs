using System.Diagnostics;
using System.IO;
using System.Security;
using DriveSpaceChecker.Models;

namespace DriveSpaceChecker.Services;

public sealed class ScanProgress
{
    public string CurrentPath { get; init; } = "";
    public long Files { get; init; }
    public long Folders { get; init; }
    public long Bytes { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public sealed class ScanResult
{
    public required FsNode Root { get; init; }
    public required string RootPath { get; init; }
    public long TotalBytes { get; init; }
    public long TotalFiles { get; init; }
    public long TotalFolders { get; init; }
    public long Inaccessible { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool IsDriveRoot { get; init; }
    public long DriveTotalBytes { get; init; }
    public long DriveFreeBytes { get; init; }
    public required List<FsNode> LargestFiles { get; init; }
    public required List<FsNode> LargestFolders { get; init; }
    public required Dictionary<string, long> Extensions { get; init; }
}

public sealed class DiskScanner
{
    private const int TopCount = 250;

    public Task<ScanResult> ScanAsync(string path, bool includeHidden,
        IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        => Task.Run(() => Scan(path, includeHidden, progress, cancellationToken), cancellationToken);

    private static ScanResult Scan(string path, bool includeHidden,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"The folder '{full}' does not exist or is not accessible.");
        }

        var stopwatch = Stopwatch.StartNew();
        var ctx = new ScanContext(progress, ct);
        var info = new DirectoryInfo(full);
        var root = new FsNode
        {
            Name = info.Name.Length > 0 ? info.Name : full,
            IsDirectory = true,
            Children = new List<FsNode>(),
            RootPath = info.FullName,
            Category = FileCategories.Other
        };

        ScanDirectory(root, includeHidden, ctx);
        SortRecursively(root);
        stopwatch.Stop();

        var rootPath = Path.GetPathRoot(full);
        var isDriveRoot = !string.IsNullOrEmpty(rootPath) &&
                          string.Equals(Path.GetFullPath(rootPath), full, StringComparison.OrdinalIgnoreCase);
        long driveTotal = 0;
        long driveFree = 0;
        if (isDriveRoot)
        {
            try
            {
                var drive = new DriveInfo(rootPath!);
                driveTotal = drive.TotalSize;
                driveFree = drive.AvailableFreeSpace;
            }
            catch
            {
                isDriveRoot = false;
            }
        }

        return new ScanResult
        {
            Root = root,
            RootPath = full,
            TotalBytes = root.Size,
            TotalFiles = root.FileCount,
            TotalFolders = root.FolderCount,
            Inaccessible = ctx.Inaccessible,
            Elapsed = stopwatch.Elapsed,
            IsDriveRoot = isDriveRoot,
            DriveTotalBytes = driveTotal,
            DriveFreeBytes = driveFree,
            LargestFiles = ctx.TopFiles.ExtractSorted(),
            LargestFolders = ctx.TopFolders.ExtractSorted(),
            Extensions = ctx.Extensions
        };
    }

    private static void ScanDirectory(FsNode node, bool includeHidden, ScanContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };

        IEnumerator<FileSystemInfo>? enumerator = null;
        try
        {
            enumerator = new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", options).GetEnumerator();
        }
        catch (Exception ex) when (IsAccessError(ex))
        {
            ctx.Inaccessible++;
            return;
        }

        using (enumerator)
        {
            while (true)
            {
                ctx.Token.ThrowIfCancellationRequested();
                FileSystemInfo entry;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    entry = enumerator.Current;
                }
                catch (Exception ex) when (IsAccessError(ex))
                {
                    ctx.Inaccessible++;
                    break;
                }

                ctx.Seen++;
                ctx.MaybeReport(node.FullPath);

                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch
                {
                    ctx.Inaccessible++;
                    continue;
                }

                if (entry is DirectoryInfo dir)
                {
                    // Do not follow junctions / symbolic links, which can create cycles.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

                    var child = new FsNode
                    {
                        Name = dir.Name,
                        IsDirectory = true,
                        Children = new List<FsNode>(),
                        Parent = node,
                        Category = FileCategories.Other
                    };
                    ScanDirectory(child, includeHidden, ctx);
                    node.Size += child.Size;
                    node.FileCount += child.FileCount;
                    node.FolderCount += child.FolderCount + 1;
                    node.Children!.Add(child);
                    ctx.TotalFolders++;
                    ctx.TopFolders.Offer(child, child.Size);
                }
                else if (entry is FileInfo file)
                {
                    long length = 0;
                    try
                    {
                        length = file.Length;
                    }
                    catch
                    {
                        ctx.Inaccessible++;
                    }

                    var extension = file.Extension;
                    var child = new FsNode
                    {
                        Name = file.Name,
                        IsDirectory = false,
                        Size = length,
                        Parent = node,
                        FileCount = 1,
                        Category = FileCategories.GetCategory(extension)
                    };
                    node.Children!.Add(child);
                    node.Size += length;
                    node.FileCount++;
                    ctx.TotalFiles++;
                    ctx.TotalBytes += length;
                    ctx.TopFiles.Offer(child, length);

                    var key = string.IsNullOrEmpty(extension) ? "(no extension)" : extension.TrimStart('.').ToLowerInvariant();
                    ctx.Extensions.TryGetValue(key, out var current);
                    ctx.Extensions[key] = current + length;
                }
            }
        }
    }

    private static bool IsAccessError(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or SecurityException or NotSupportedException or ArgumentException;

    private static void SortRecursively(FsNode node)
    {
        if (node.Children is not { Count: > 0 }) return;
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var child in node.Children)
        {
            SortRecursively(child);
        }
        var biggest = node.Children[0];
        node.Category = biggest.Category;
    }

    private sealed class ScanContext
    {
        private readonly IProgress<ScanProgress>? _progress;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _lastReportMs;

        public ScanContext(IProgress<ScanProgress>? progress, CancellationToken token)
        {
            _progress = progress;
            Token = token;
        }

        public CancellationToken Token { get; }
        public long Seen { get; set; }
        public long TotalFiles { get; set; }
        public long TotalBytes { get; set; }
        public long TotalFolders { get; set; }
        public long Inaccessible { get; set; }
        public BoundedTopList TopFiles { get; } = new(TopCount);
        public BoundedTopList TopFolders { get; } = new(TopCount);
        public Dictionary<string, long> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void MaybeReport(string currentPath)
        {
            if (_progress == null) return;
            var now = _watch.ElapsedMilliseconds;
            if (now - _lastReportMs < 250) return;
            _lastReportMs = now;
            _progress.Report(new ScanProgress
            {
                CurrentPath = currentPath,
                Files = TotalFiles,
                Folders = TotalFolders,
                Bytes = TotalBytes,
                Elapsed = _watch.Elapsed
            });
        }
    }

    private sealed class BoundedTopList
    {
        private readonly int _capacity;
        private readonly PriorityQueue<FsNode, long> _queue = new();

        public BoundedTopList(int capacity) => _capacity = capacity;

        public void Offer(FsNode node, long size)
        {
            if (size <= 0) return;
            _queue.Enqueue(node, size);
            if (_queue.Count > _capacity) _queue.Dequeue();
        }

        public List<FsNode> ExtractSorted()
        {
            var list = new List<FsNode>(_queue.Count);
            while (_queue.TryDequeue(out var node, out _)) list.Add(node);
            list.Sort((a, b) => b.Size.CompareTo(a.Size));
            return list;
        }
    }
}
