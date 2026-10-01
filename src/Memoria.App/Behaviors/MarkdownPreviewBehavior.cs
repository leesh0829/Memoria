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
    private static readonly DependencyProperty PendingRenderProperty =
        DependencyProperty.RegisterAttached("PendingRender", typeof(DispatcherOperation), typeof(MarkdownPreviewBehavior));
    private static readonly DependencyProperty LifecycleRegisteredProperty =
        DependencyProperty.RegisterAttached("LifecycleRegistered", typeof(bool), typeof(MarkdownPreviewBehavior));

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
        if (!(bool)viewer.GetValue(LifecycleRegisteredProperty))
        {
            viewer.SetValue(LifecycleRegisteredProperty, true);
            viewer.Loaded += OnLoaded;
            viewer.Unloaded += OnUnloaded;
        }
        QueueRender(viewer);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
        => QueueRender((FlowDocumentScrollViewer)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var viewer = (FlowDocumentScrollViewer)sender;
        CancelRender(viewer);
        viewer.SetValue(RenderGenerationProperty, (int)viewer.GetValue(RenderGenerationProperty) + 1);
        viewer.Document = null;
    }

    private static void CancelRender(FlowDocumentScrollViewer viewer)
    {
        if (viewer.GetValue(PendingRenderProperty) is DispatcherOperation pending)
            pending.Abort();
        viewer.ClearValue(PendingRenderProperty);
    }

    private static void QueueRender(FlowDocumentScrollViewer viewer)
    {
        CancelRender(viewer);

        // 바인딩 전파 중 Markdown/Active/RenderMode가 연달아 바뀌어도 마지막 상태만 렌더한다.
        // Background 우선순위로 한 번 양보해 노트 선택/레이아웃이 먼저 화면에 반영되도록 한다.
        var generation = (int)viewer.GetValue(RenderGenerationProperty) + 1;
        viewer.SetValue(RenderGenerationProperty, generation);
        if (!GetActive(viewer) || !viewer.IsLoaded)
        {
            if (viewer.Document is not null) viewer.Document = null;
            return;
        }

        var operation = viewer.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            viewer.ClearValue(PendingRenderProperty);
            if ((int)viewer.GetValue(RenderGenerationProperty) != generation
                || !GetActive(viewer) || !viewer.IsLoaded) return;

            var renderer = AppServices.Resolve<IMarkdownRenderer>();
            // Active may fire before the Markdown binding propagates → GetMarkdown null.
            var text = GetMarkdown(viewer) ?? string.Empty;
            var mode = GetRenderMode(viewer);
            using var perf = AppLog.Measure($"Markdown.Preview mode={mode} chars={text.Length}", 100);
            viewer.Document = mode == "read"
                ? renderer.RenderRead(text)
                : renderer.Render(text);
        }));
        viewer.SetValue(PendingRenderProperty, operation);
    }
}
