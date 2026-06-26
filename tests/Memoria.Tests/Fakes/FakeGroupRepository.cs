using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.Fakes;

public sealed class FakeGroupRepository : IGroupRepository
{
    public readonly List<Group> Items = new();
    private int _nextId = 1;

    /// Backward-compat alias so existing tests that seed via _groups.Groups still work.
    public List<Group> Groups => Items;

    public int Create(Group group)
    {
        group.Id = _nextId++;
        Items.Add(group);
        return group.Id;
    }

    public void Update(Group group)
    {
        var i = Items.FindIndex(g => g.Id == group.Id);
        if (i >= 0) Items[i] = group;
    }

    public void Delete(int id) => Items.RemoveAll(g => g.Id == id);

    public Group? Get(int id) => Items.FirstOrDefault(g => g.Id == id);

    public IReadOnlyList<Group> GetAll() => Items.OrderBy(g => g.SortOrder).ToList();
}
