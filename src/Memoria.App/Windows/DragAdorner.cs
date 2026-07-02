using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Memoria.App.ViewModels;

namespace Memoria.App.Windows;

/// <summary>
/// Ghost drag adorner — follows the cursor with a translucent snapshot of the dragged TreeViewItem row.
/// All paths are try/catch-guarded so any rendering failure degrades silently without breaking DragDrop.
/// Opacity ~0.7, IsHitTestVisible=false.
/// </summary>
public sealed class DragAdorner : Adorner
{
    private readonly ImageSource? _image;
    private readonly double _imgW;
    private readonly double _imgH;
    private Point _pos;

    private DragAdorner(UIElement adornedElement, ImageSource image, double w, double h)
        : base(adornedElement)
    {
        _image = image;
        _imgW  = w;
        _imgH  = h;
        IsHitTestVisible = false;
        Opacity = 0.7;
    }

    /// <summary>
    /// Snapshots <paramref name="dragVisual"/> (capped at 28 px height to avoid showing expanded children),
    /// adds the adorner to the AdornerLayer of <paramref name="adornedRoot"/>.
    /// Returns <c>null</c> on any failure — caller continues without the ghost.
    /// </summary>
    public static DragAdorner? TryCreate(UIElement adornedRoot, UIElement dragVisual)
    {
        try
        {
            var pxW = (int)Math.Max(1, Math.Ceiling(dragVisual.RenderSize.Width));
            var pxH = (int)Math.Max(1, Math.Min(28, Math.Ceiling(dragVisual.RenderSize.Height)));

            var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
            var dv  = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Stretch=None + AlignmentY=Top → shows only the header row, clipping children.
                var vb = new VisualBrush(dragVisual)
                {
                    Stretch    = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, pxW, pxH));
            }
            rtb.Render(dv);
            rtb.Freeze();

            var layer = AdornerLayer.GetAdornerLayer(adornedRoot);
            if (layer is null) return null;

            var adorner = new DragAdorner(adornedRoot, rtb, pxW, pxH);
            layer.Add(adorner);
            return adorner;
        }
        catch { return null; }
    }

    /// <summary>Moves the ghost to follow the cursor. Call from DragOver or GiveFeedback.</summary>
    public void Update(Point mousePositionOnAdornedElement)
    {
        try { _pos = mousePositionOnAdornedElement; InvalidateVisual(); }
        catch { /* degrade gracefully */ }
    }

    /// <summary>Removes the adorner from the layer. Safe to call multiple times.</summary>
    public void Remove()
    {
        try { AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this); }
        catch { /* degrade gracefully */ }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        try
        {
            if (_image is null) return;
            // Offset the ghost slightly from cursor so it does not block the hit-test element.
            drawingContext.DrawImage(_image, new Rect(_pos.X + 10, _pos.Y + 4, _imgW, _imgH));
        }
        catch { /* degrade gracefully */ }
    }
}

/// <summary>
/// Drop indicator adorner — draws an insertion line (Before/After) or a fill highlight (Into)
/// at the position of the current drop target TreeViewItem.
/// All paths are try/catch-guarded.
/// </summary>
public sealed class DropIndicatorAdorner : Adorner
{
    private UIElement? _target;
    private DropZone   _zone;

    private static readonly Pen    _linePen;
    private static readonly Brush  _fillBrush;

    static DropIndicatorAdorner()
    {
        _linePen = new Pen(new SolidColorBrush(Color.FromRgb(80, 140, 240)) { Opacity = 0.9 }, 2);
        _linePen.Freeze();
        _fillBrush = new SolidColorBrush(Color.FromArgb(40, 80, 140, 240));
        _fillBrush.Freeze();
    }

    private DropIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    /// <summary>Creates and adds the adorner to GroupTree's AdornerLayer. Returns <c>null</c> on failure.</summary>
    public static DropIndicatorAdorner? TryCreate(UIElement adornedRoot)
    {
        try
        {
            var layer = AdornerLayer.GetAdornerLayer(adornedRoot);
            if (layer is null) return null;
            var a = new DropIndicatorAdorner(adornedRoot);
            layer.Add(a);
            return a;
        }
        catch { return null; }
    }

    /// <summary>Updates the indicator to the new target and zone.</summary>
    public void Update(UIElement target, DropZone zone)
    {
        try { _target = target; _zone = zone; InvalidateVisual(); }
        catch { /* degrade gracefully */ }
    }

    /// <summary>Hides the indicator (keeps adorner alive for reuse).</summary>
    public void Clear()
    {
        try { _target = null; InvalidateVisual(); }
        catch { /* degrade gracefully */ }
    }

    /// <summary>Removes the adorner from the layer. Safe to call multiple times.</summary>
    public void Remove()
    {
        try { AdornerLayer.GetAdornerLayer(AdornedElement)?.Remove(this); }
        catch { /* degrade gracefully */ }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        try
        {
            if (_target is null) return;

            // TransformToAncestor can throw if _target was removed from the tree.
            var transform = _target.TransformToAncestor(AdornedElement);
            var origin    = transform.Transform(new Point(0, 0));
            var fullW     = AdornedElement.RenderSize.Width;
            var itemH     = _target.RenderSize.Height;

            // Indent the line start to align with the item text (not the expander area).
            const double lineIndent = 16.0;

            switch (_zone)
            {
                case DropZone.Before:
                    drawingContext.DrawLine(_linePen,
                        new Point(origin.X + lineIndent, origin.Y),
                        new Point(origin.X + fullW,      origin.Y));
                    break;

                case DropZone.After:
                    drawingContext.DrawLine(_linePen,
                        new Point(origin.X + lineIndent, origin.Y + itemH),
                        new Point(origin.X + fullW,      origin.Y + itemH));
                    break;

                case DropZone.Into:
                    // Highlight just the header row (capped at 24 px).
                    var headerH = Math.Min(24.0, itemH);
                    drawingContext.DrawRoundedRectangle(
                        _fillBrush, _linePen,
                        new Rect(origin.X, origin.Y, Math.Max(0, fullW - origin.X), headerH),
                        3, 3);
                    break;
            }
        }
        catch { /* degrade gracefully — disconnect from tree, etc. */ }
    }
}
