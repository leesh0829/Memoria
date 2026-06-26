using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Core.Services;

public sealed class TaggingService : ITaggingService
{
    private readonly IClientClassifier _classifier;
    private readonly IClientRepository _clients;

    public TaggingService(IClientClassifier classifier, IClientRepository clients)
    {
        _classifier = classifier;
        _clients = clients;
    }

    public ChecklistItem ApplyAutoTag(ChecklistItem item)
    {
        if (item.Kind != ItemKind.Task || item.IsManual) return item;

        var rules = _clients.GetRules();
        var enabledIds = _clients.GetAll(enabledOnly: true).Select(c => c.Id).ToHashSet();
        item.ClientId = _classifier.Classify(item.Text, rules, enabledIds);
        return item;
    }
}
