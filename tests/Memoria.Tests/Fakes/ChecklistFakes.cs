// tests/Memoria.Tests/Fakes/ChecklistFakes.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;

namespace Memoria.Tests.Fakes;

internal sealed class FakeChecklistRepository : IChecklistRepository
{
    public readonly List<ChecklistItem> Items = new();
    private int _seq = 100;

    public int AddItem(ChecklistItem item)
    {
        item.Id = ++_seq;
        Items.Add(Clone(item));
        return item.Id;
    }

    public void UpdateItem(ChecklistItem item)
    {
        var idx = Items.FindIndex(i => i.Id == item.Id);
        if (idx >= 0) Items[idx] = Clone(item);
    }

    public void DeleteItem(int id) => Items.RemoveAll(i => i.Id == id);

    public IReadOnlyList<ChecklistItem> GetByNote(int noteId) =>
        Items.Where(i => i.NoteId == noteId)
             .OrderBy(i => i.SortOrder)
             .Select(Clone)
             .ToList();

    private static ChecklistItem Clone(ChecklistItem i) => new()
    {
        Id = i.Id, NoteId = i.NoteId, Kind = i.Kind, Text = i.Text, Done = i.Done,
        DoneAt = i.DoneAt, ClientId = i.ClientId, IsManual = i.IsManual,
        SortOrder = i.SortOrder, CreatedAt = i.CreatedAt, UpdatedAt = i.UpdatedAt,
    };
}

internal sealed class FakeClientRepository : IClientRepository
{
    public readonly List<Client> Clients = new();

    public int Create(Client client) { client.Id = Clients.Count + 1; Clients.Add(client); return client.Id; }
    public void Update(Client client) { }
    public void Delete(int id) { }

    public IReadOnlyList<Client> GetAll(bool enabledOnly = false) =>
        Clients.Where(c => !enabledOnly || c.Enabled)
               .OrderBy(c => c.SortOrder)
               .ToList();

    public IReadOnlyList<ClientRule> GetRules() => new List<ClientRule>();
    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules) { }
}

/// 계약 §5 ITaggingService 의미를 모사: Task & !IsManual 일 때만 키워드로 ClientId 재계산.
internal sealed class FakeTaggingService : ITaggingService
{
    public readonly Dictionary<string, int> KeywordToClient = new(StringComparer.OrdinalIgnoreCase);

    public ChecklistItem ApplyAutoTag(ChecklistItem item)
    {
        if (item.Kind != ItemKind.Task || item.IsManual) return item;
        item.ClientId = null;
        foreach (var pair in KeywordToClient)
        {
            if (item.Text.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                item.ClientId = pair.Value;
                break;
            }
        }
        return item;
    }
}
