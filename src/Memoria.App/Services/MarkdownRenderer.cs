using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Memoria.Core.Attachments;
using WBlock = System.Windows.Documents.Block;
using WInline = System.Windows.Documents.Inline;

namespace Memoria.App.Services;

/// <summary>Markdig AST → WPF FlowDocument. 테마 브러시 연동, 실패 시 원문 폴백.</summary>
public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly FontFamily UiFont =
        new FontFamily("Segoe UI, Malgun Gothic");

    private readonly IAttachmentService _attachments;
    private readonly MarkdownPipeline _pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public MarkdownRenderer(IAttachmentService attachments) => _attachments = attachments;

    public FlowDocument Render(string? markdown)
    {
        var flow = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14, FontFamily = UiFont };
        flow.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Foreground");
        try
        {
            var doc = Markdown.Parse(markdown ?? "", _pipeline);
            foreach (var block in doc)
                if (ConvertBlock(block) is { } b) flow.Blocks.Add(b);
        }
        catch
        {
            flow.Blocks.Clear();
            flow.Blocks.Add(new Paragraph(new Run(markdown ?? "")));
        }
        return flow;
    }

    public FlowDocument RenderRead(string? markdown)
    {
        var flow = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14, FontFamily = UiFont };
        flow.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Foreground");
        try
        {
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var seg in Memoria.Core.Text.MarkdownReadSegmenter.Segment(markdown))
            {
                if (seg.IsImage)
                {
                    para.Inlines.Add(BuildReadImage(seg.Value));
                }
                else
                {
                    // 텍스트 그대로(줄바꿈 보존).
                    var lines = seg.Value.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        para.Inlines.Add(new Run(lines[i]));
                        if (i < lines.Length - 1) para.Inlines.Add(new LineBreak());
                    }
                }
            }
            flow.Blocks.Add(para);
        }
        catch
        {
            flow.Blocks.Clear();
            flow.Blocks.Add(new Paragraph(new Run(markdown ?? "")));
        }
        return flow;
    }

    private WInline BuildReadImage(string relPath)
    {
        try
        {
            var abs = _attachments.ResolveToAbsolute(relPath);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(abs);
            bmp.EndInit();
            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 600,
                MaxHeight = 400,
            };
            return new InlineUIContainer(img);
        }
        catch { return new Run($"[이미지: {relPath}]"); }
    }

    private WBlock? ConvertBlock(Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var p = new Paragraph { FontWeight = FontWeights.Bold };
                p.FontSize = h.Level switch { 1 => 22, 2 => 19, 3 => 17, _ => 15 };
                p.Margin = new Thickness(0, 8, 0, 4);
                AddInlines(p.Inlines, h.Inline);
                return p;
            }
            case ParagraphBlock para:
            {
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
                AddInlines(p.Inlines, para.Inline);
                return p;
            }
            case QuoteBlock quote:
            {
                var section = new Section { Margin = new Thickness(8, 2, 0, 6) };
                section.SetResourceReference(Section.ForegroundProperty, "Brush.SecondaryForeground");
                section.BorderThickness = new Thickness(3, 0, 0, 0);
                section.SetResourceReference(Section.BorderBrushProperty, "Brush.Border");
                section.Padding = new Thickness(8, 0, 0, 0);
                foreach (var child in quote)
                    if (ConvertBlock(child) is { } cb) section.Blocks.Add(cb);
                return section;
            }
            case ListBlock list:
            {
                var wlist = new List
                {
                    MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 2, 0, 6),
                    Padding = new Thickness(20, 0, 0, 0),
                };
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var li = new ListItem();
                    foreach (var child in item)
                        if (ConvertBlock(child) is { } cb) li.Blocks.Add(cb);
                    if (li.Blocks.Count == 0) li.Blocks.Add(new Paragraph());
                    wlist.ListItems.Add(li);
                }
                return wlist;
            }
            case CodeBlock code:
            {
                var text = string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString()));
                var p = new Paragraph(new Run(text))
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Margin = new Thickness(0, 2, 0, 6),
                    Padding = new Thickness(8),
                };
                p.SetResourceReference(Paragraph.BackgroundProperty, "Brush.ListItemHover");
                return p;
            }
            case ThematicBreakBlock:
            {
                var p = new Paragraph { Margin = new Thickness(0, 6, 0, 6) };
                p.Inlines.Add(new Run(new string('─', 20)));
                p.SetResourceReference(Paragraph.ForegroundProperty, "Brush.Border");
                return p;
            }
            default:
                return null;
        }
    }

    private void AddInlines(InlineCollection target, ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
            if (ConvertInline(inline) is { } wi) target.Add(wi);
    }

    private WInline? ConvertInline(Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                return new Run(lit.Content.ToString());
            case LineBreakInline:
                return new LineBreak();
            case CodeInline ci:
            {
                var run = new Run(ci.Content) { FontFamily = new FontFamily("Consolas, Courier New, monospace") };
                return run;
            }
            case EmphasisInline em:
            {
                Span span = em.DelimiterCount >= 2 ? new Bold() : new Italic();
                foreach (var child in em)
                    if (ConvertInline(child) is { } wi) span.Inlines.Add(wi);
                return span;
            }
            case LinkInline link when link.IsImage:
                return BuildImage(link);
            case LinkInline link:
            {
                var h = new Hyperlink { NavigateUri = SafeUri(link.Url) };
                h.SetResourceReference(Hyperlink.ForegroundProperty, "Brush.Accent");
                foreach (var child in link)
                    if (ConvertInline(child) is { } wi) h.Inlines.Add(wi);
                if (h.Inlines.Count == 0) h.Inlines.Add(new Run(link.Url ?? ""));
                return h;
            }
            default:
                // 기타 컨테이너 인라인은 자식만 펼친다.
                if (inline is ContainerInline c)
                {
                    var span = new Span();
                    foreach (var child in c)
                        if (ConvertInline(child) is { } wi) span.Inlines.Add(wi);
                    return span;
                }
                return null;
        }
    }

    private WInline BuildImage(LinkInline link)
    {
        try
        {
            var abs = _attachments.ResolveToAbsolute(link.Url ?? "");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(abs);
            bmp.EndInit();
            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 600,
                MaxHeight = 400,
            };
            return new InlineUIContainer(img);
        }
        catch
        {
            return new Run($"[이미지: {link.Url}]");
        }
    }

    private static Uri? SafeUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var u) ? u : null;
    }
}
