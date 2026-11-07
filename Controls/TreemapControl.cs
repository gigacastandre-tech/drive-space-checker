using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DriveSpaceChecker.Models;
using DriveSpaceChecker.Services;

namespace DriveSpaceChecker.Controls;

/// <summary>
/// WinDirStat-style treemap: renders the direct children of the current folder as proportional
/// rectangles. Double-click a folder to zoom in, double-click a file to open it.
/// </summary>
public sealed class TreemapControl : FrameworkElement
{
    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(248, 250, 252)));
    private static readonly Brush HintBrush = Freeze(new SolidColorBrush(Color.FromRgb(148, 163, 184)));
    private static readonly Brush DarkTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(15, 23, 42)));
    private static readonly Brush LightTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 255, 255)));
    private static readonly Brush InfoBackground = Freeze(new SolidColorBrush(Color.FromArgb(235, 15, 23, 42)));
    private static readonly Brush InfoMuted = Freeze(new SolidColorBrush(Color.FromRgb(203, 213, 225)));
    private static readonly Brush InfoPathBrush = Freeze(new SolidColorBrush(Color.FromRgb(148, 163, 184)));
    private static readonly Pen HoverPen = Freeze(new Pen(Brushes.White, 2.2));
    private static readonly Pen HoverShadow = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 3.2));
    private static readonly Pen BorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(35, 15, 23, 42)), 1));

    private ScanResult? _result;
    private FsNode? _current;
    private List<TreemapSlice> _slices = new();
    private DrawingGroup? _baseDrawing;
    private int _hover = -1;
    private int _selected = -1;

    public TreemapControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;
        Cursor = Cursors.Arrow;
    }

    public event Action<FsNode>? NodeActivated;
    public event Action<FsNode?>? NodeSelected;
    public event Action<FsNode, Point>? ContextRequested;

    public FsNode? CurrentNode => _current;

    public void SetData(FsNode? current, ScanResult? result)
    {
        _current = current;
        _result = result;
        _hover = -1;
        Rebuild();
        InvalidateVisual();
    }

    public void Clear()
    {
        _result = null;
        _current = null;
        _slices.Clear();
        _baseDrawing = null;
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Rebuild();
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Rebuild();
        InvalidateVisual();
    }

    private void Rebuild()
    {
        _baseDrawing = null;
        _slices.Clear();
        if (_result == null || _current == null) return;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 2 || height < 2) return;

        var items = TreemapLayout.BuildItems(_current, _result);
        _slices = TreemapLayout.Layout(items, new Rect(0, 0, width, height));
        if (_slices.Count == 0) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            foreach (var slice in _slices)
            {
                var item = slice.Item;
                var rect = slice.Rect;
                if (rect.Width < 0.5 || rect.Height < 0.5) continue;
                if (rect.Width > 2.5 && rect.Height > 2.5)
                {
                    rect = new Rect(rect.X + 0.75, rect.Y + 0.75, rect.Width - 1.5, rect.Height - 1.5);
                }

                dc.DrawRectangle(FileCategories.BrushFor(item.Category), null, rect);

                if (rect.Width > 55 && rect.Height > 20)
                {
                    var textBrush = FileCategories.UseDarkText(item.Category) ? DarkTextBrush : LightTextBrush;
                    var name = TrimName(item.Name, rect.Width - 10, 11.5, dpi, textBrush, SemiBold);
                    dc.DrawText(name, new Point(rect.X + 5, rect.Y + 4));

                    if (rect.Height > 38)
                    {
                        var size = MakeText(Format.Bytes(item.Size) + "  ·  " + Format.Percent(item.Percent),
                            10.5, textBrush, dpi, Regular);
                        size.MaxTextWidth = Math.Max(1, rect.Width - 10);
                        size.MaxLineCount = 1;
                        size.Trimming = TextTrimming.CharacterEllipsis;
                        dc.DrawText(size, new Point(rect.X + 5, rect.Y + 20));
                    }
                }
                else if (rect.Width > 3 && rect.Height > 3)
                {
                    dc.DrawRectangle(null, BorderPen, rect);
                }
            }
        }
        group.Freeze();
        _baseDrawing = group;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 1 || height < 1) return;

        if (_baseDrawing == null)
        {
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
            if (_result == null)
            {
                DrawHint(dc, "Scan a drive or folder to see where the space is used.", width, height);
            }
            else
            {
                DrawHint(dc, "This folder is empty.", width, height);
            }
            return;
        }

        dc.DrawDrawing(_baseDrawing);

        if (_hover >= 0 && _hover < _slices.Count)
        {
            var rect = _slices[_hover].Rect;
            var inset = new Rect(rect.X + 1, rect.Y + 1, Math.Max(0, rect.Width - 2), Math.Max(0, rect.Height - 2));
            dc.DrawRectangle(null, HoverShadow, inset);
            dc.DrawRectangle(null, HoverPen, inset);
            DrawInfoBar(dc, _slices[_hover].Item, width, height);
        }
    }

    private void DrawInfoBar(DrawingContext dc, TreemapItem item, double width, double height)
    {
        var barWidth = Math.Max(0, width - 16);
        if (barWidth < 80) return;
        var barHeight = 54.0;
        var rect = new Rect(8, height - barHeight - 8, barWidth, barHeight);
        dc.DrawRoundedRectangle(InfoBackground, null, rect, 8, 8);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var title = MakeText(item.Name, 12.5, LightTextBrush, dpi, SemiBold);
        title.MaxTextWidth = Math.Max(1, barWidth - 190);
        title.MaxLineCount = 1;
        title.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(title, new Point(rect.X + 12, rect.Y + 8));

        var sizeText = MakeText($"{Format.Bytes(item.Size)}  ·  {Format.Percent(item.Percent)} of this folder",
            11.5, InfoMuted, dpi, Regular);
        sizeText.MaxTextWidth = 165;
        sizeText.MaxLineCount = 1;
        sizeText.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(sizeText, new Point(rect.Right - 177, rect.Y + 9));

        var path = item.IsFree
            ? "Unused space on this drive"
            : item.IsAggregate
                ? "Smaller items grouped together"
                : item.Node?.FullPath ?? item.Name;
        var pathText = MakeText(path, 10.5, InfoPathBrush, dpi, Regular);
        pathText.MaxTextWidth = Math.Max(1, barWidth - 24);
        pathText.MaxLineCount = 1;
        pathText.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(pathText, new Point(rect.X + 12, rect.Y + 31));
    }

    private void DrawHint(DrawingContext dc, string text, double width, double height)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formatted = MakeText(text, 13, HintBrush, dpi, Regular);
        formatted.TextAlignment = TextAlignment.Center;
        formatted.MaxTextWidth = Math.Max(1, width - 40);
        dc.DrawText(formatted, new Point(width / 2 - formatted.MaxTextWidth / 2, height / 2 - 10));
    }

    private static FormattedText MakeText(string text, double size, Brush brush, double dpi, Typeface typeface)
    {
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size,
            brush, dpi);
    }

    private static FormattedText TrimName(string text, double maxWidth, double size, double dpi, Brush brush,
        Typeface typeface)
    {
        var formatted = MakeText(text, size, brush, dpi, typeface);
        formatted.MaxTextWidth = Math.Max(1, maxWidth);
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }

    private int HitTest(Point point)
    {
        for (var i = 0; i < _slices.Count; i++)
        {
            if (_slices[i].Rect.Contains(point)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        var index = HitTest(position);
        if (index != _hover)
        {
            _hover = index;
            Cursor = index >= 0 && _slices[index].Item.Node != null ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1)
        {
            _hover = -1;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var index = HitTest(e.GetPosition(this));
        if (index < 0) return;
        var item = _slices[index].Item;
        _selected = index;
        NodeSelected?.Invoke(item.Node);
        if (e.ClickCount >= 2 && item.Node != null)
        {
            NodeActivated?.Invoke(item.Node);
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var index = HitTest(e.GetPosition(this));
        if (index < 0) return;
        var node = _slices[index].Item.Node;
        if (node == null) return;
        _selected = index;
        NodeSelected?.Invoke(node);
        ContextRequested?.Invoke(node, PointToScreen(e.GetPosition(this)));
        e.Handled = true;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
