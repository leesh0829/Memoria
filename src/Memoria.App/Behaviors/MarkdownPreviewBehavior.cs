using System.Windows;
using System.Windows.Controls;
using Memoria.App.Services;

namespace Memoria.App.Behaviors;

/// <summary>FlowDocumentScrollViewer.Markdown/Active 첨부 속성 → 렌더러로 Document 갱신.</summary>
public static class MarkdownPreviewBehavior
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached("Markdown", typeof(string), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ActiveProperty =
        DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata(false, OnChanged));

    public static void SetMarkdown(DependencyObject o, string v) => o.SetValue(MarkdownProperty, v);
    public static string GetMarkdown(DependencyObject o) => (string)o.GetValue(MarkdownProperty);
    public static void SetActive(DependencyObject o, bool v) => o.SetValue(ActiveProperty, v);
    public static bool GetActive(DependencyObject o) => (bool)o.GetValue(ActiveProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowDocumentScrollViewer viewer) return;
        if (!GetActive(viewer)) { viewer.Document = null; return; }
        var renderer = AppServices.Resolve<IMarkdownRenderer>();
        // Active may fire before the Markdown binding propagates → GetMarkdown null.
        // Render already guards null, but decouple the behavior from that contract.
        viewer.Document = renderer.Render(GetMarkdown(viewer) ?? string.Empty);
    }
}
