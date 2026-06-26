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

internal sealed class FakeNoteRepository : INoteRepository
{
    public readonly List<Note> Notes = new();
    private int _seq = 0;

    public int Create(Note note) { note.Id = ++_seq; Notes.Add(note); return note.Id; }
    public void Update(Note note)
    {
        var idx = Notes.FindIndex(n => n.Id == note.Id);
        if (idx >= 0) Notes[idx] = note; else Notes.Add(note);
    }
    public void SoftDelete(int id) { }
    public void Restore(int id) { }
    public void Purge(int id) { }
    public void PurgeExpiredTrash(int retentionDays) { }
    public Note? Get(int id) => Notes.FirstOrDefault(n => n.Id == id);
    public IReadOnlyList<Note> GetByGroup(int? groupId) => new List<Note>();
    public IReadOnlyList<Note> GetTrash() => new List<Note>();
    public IReadOnlyList<Note> GetChecklistsInWeek(DateOnly monday, DateOnly friday) => new List<Note>();
    public Note? FindWeeklyReport(DateOnly weekStart, ReportFormatKind format) => null;
}

internal sealed class FakeGroupRepository : IGroupRepository
{
    public readonly List<Group> Groups = new();
    public int Create(Group group) { group.Id = Groups.Count + 1; Groups.Add(group); return group.Id; }
    public void Update(Group group) { }
    public void Delete(int id) { }
    public Group? Get(int id) => Groups.FirstOrDefault(g => g.Id == id);
    public IReadOnlyList<Group> GetAll() => Groups.OrderBy(g => g.SortOrder).ToList();
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
