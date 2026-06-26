using Memoria.Core.Models;

namespace Memoria.Core.Classification;

public sealed class ClientClassifier : IClientClassifier
{
    public int? Classify(string taskText, IEnumerable<ClientRule> rules, ISet<int> enabledClientIds)
    {
        foreach (var rule in rules
                     .Where(r => enabledClientIds.Contains(r.ClientId))
                     .OrderBy(r => r.Priority))
        {
            if (taskText.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                return rule.ClientId;
        }
        return null;
    }
}
