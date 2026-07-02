using System.Collections.Generic;
using System.Linq;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.App.Fakes;

internal sealed class FakeGroupRepository : IGroupRepository
{
    public List<Group> Items { get; } = new();

    public int Create(Group group) { group.Id = Items.Count + 1; Items.Add(group); return group.Id; }
    public void Update(Group group) { }
    public void Delete(int id) => Items.RemoveAll(g => g.Id == id);
    public Group? Get(int id) => Items.FirstOrDefault(g => g.Id == id);
    public IReadOnlyList<Group> GetAll() => Items.OrderBy(g => g.SortOrder).ToList();

    public bool IsDescendantOf(int nodeId, int ancestorId)
    {
        var visited = new HashSet<int>();
        var cur = nodeId;
        while (true)
        {
            var g = Items.FirstOrDefault(x => x.Id == cur);
            if (g?.ParentId is not int p) return false;
            if (!visited.Add(cur)) return false;
            if (p == ancestorId) return true;
            cur = p;
        }
    }
}
