using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Memoria.App.Services;

namespace Memoria.App.Behaviors;

/// <summary>FlowDocumentScrollViewer.Markdown/Active 첨부 속성 → 렌더러로 Document 갱신.</summary>
public static class MarkdownPreviewBehavior
{
    private static readonly DependencyProperty RenderGenerationProperty =
        DependencyProperty.RegisterAttached("RenderGeneration", typeof(int), typeof(MarkdownPreviewBehavior));

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached("Markdown", typeof(string), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ActiveProperty =
        DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty RenderModeProperty =
        DependencyProperty.RegisterAttached("RenderMode", typeof(string), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata("rendered", OnChanged));

    public static void SetMarkdown(DependencyObject o, string v) => o.SetValue(MarkdownProperty, v);
    public static string GetMarkdown(DependencyObject o) => (string)o.GetValue(MarkdownProperty);
    public static void SetActive(DependencyObject o, bool v) => o.SetValue(ActiveProperty, v);
    public static bool GetActive(DependencyObject o) => (bool)o.GetValue(ActiveProperty);
    public static void SetRenderMode(DependencyObject o, string v) => o.SetValue(RenderModeProperty, v);
    public static string GetRenderMode(DependencyObject o) => (string)o.GetValue(RenderModeProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowDocumentScrollViewer viewer) return;

        // 바인딩 전파 중 Markdown/Active/RenderMode가 연달아 바뀌어도 마지막 상태만 렌더한다.
        // Background 우선순위로 한 번 양보해 노트 선택/레이아웃이 먼저 화면에 반영되도록 한다.
        var generation = (int)viewer.GetValue(RenderGenerationProperty) + 1;
        viewer.SetValue(RenderGenerationProperty, generation);
        if (!GetActive(viewer))
        {
            if (viewer.Document is not null) viewer.Document = null;
            return;
        }

        viewer.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if ((int)viewer.GetValue(RenderGenerationProperty) != generation || !GetActive(viewer)) return;

            var renderer = AppServices.Resolve<IMarkdownRenderer>();
            // Active may fire before the Markdown binding propagates → GetMarkdown null.
            var text = GetMarkdown(viewer) ?? string.Empty;
            viewer.Document = GetRenderMode(viewer) == "read"
                ? renderer.RenderRead(text)
                : renderer.Render(text);
        }));
    }
}
