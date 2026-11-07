using System.IO;

namespace DriveSpaceChecker.Models;

public sealed class FsNode
{
    public string Name { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; set; }
    public int Category { get; set; }
    public FsNode? Parent { get; set; }
    public List<FsNode>? Children { get; set; }
    /// <summary>Number of files in this subtree (1 for a file).</summary>
    public long FileCount { get; set; }
    /// <summary>Number of sub-folders in this subtree (0 for a file).</summary>
    public long FolderCount { get; set; }
    /// <summary>Set on the scan root only.</summary>
    public string? RootPath { get; init; }

    public string FullPath => Parent == null
        ? (RootPath ?? Name)
        : Path.Combine(Parent.FullPath, Name);

    public string Extension => IsDirectory ? "" : Path.GetExtension(Name).TrimStart('.');

    public string TypeText => IsDirectory ? "Folder" : (string.IsNullOrEmpty(Extension) ? "File" : Extension.ToUpperInvariant() + " file");

    public double PercentOfParent =>
        Parent == null || Parent.Size <= 0 ? 0 : Size * 100.0 / Parent.Size;

    /// <summary>Removes this node from its parent and adjusts all ancestor totals.</summary>
    public void RemoveFromParent()
    {
        var parent = Parent;
        if (parent?.Children == null) return;
        parent.Children.Remove(this);
        var files = FileCount;
        var folders = FolderCount + (IsDirectory ? 1 : 0);
        for (var p = parent; p != null; p = p.Parent)
        {
            p.Size -= Size;
            p.FileCount -= files;
            p.FolderCount -= folders;
            if (p.Size < 0) p.Size = 0;
            if (p.FileCount < 0) p.FileCount = 0;
            if (p.FolderCount < 0) p.FolderCount = 0;
        }
        Parent = null;
    }
}
