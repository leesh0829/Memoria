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

    public void SetParent(int groupId, int? parentId)
    {
        var self = Items.FirstOrDefault(g => g.Id == groupId);
        if (self is null || self.IsSystem) return;
        if (parentId is int pid)
        {
            if (pid == groupId) return;
            var parent = Items.FirstOrDefault(g => g.Id == pid);
            if (parent is null || parent.IsSystem) return;
            if (IsDescendantOf(pid, groupId)) return;
        }
        self.ParentId = parentId;
        Renumber(parentId);
    }

    public void ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds)
    {
        for (var i = 0; i < orderedGroupIds.Count; i++)
        {
            var g = Items.First(x => x.Id == orderedGroupIds[i]);
            g.ParentId = parentId; g.SortOrder = i;
        }
    }

    private void Renumber(int? parentId)
    {
        var sibs = Items.Where(g => g.ParentId == parentId).OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToList();
        for (var i = 0; i < sibs.Count; i++) sibs[i].SortOrder = i;
    }
}
