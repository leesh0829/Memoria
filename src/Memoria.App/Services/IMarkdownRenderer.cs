using System.Windows.Documents;

namespace Memoria.App.Services;

public interface IMarkdownRenderer
{
    FlowDocument Render(string? markdown);
    FlowDocument RenderRead(string? markdown);
}
