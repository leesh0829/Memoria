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

    public void Delete(int id)
    {
        var t = Items.FirstOrDefault(g => g.Id == id);
        if (t is null) return;
        var np = t.ParentId;
        foreach (var c in Items.Where(g => g.ParentId == id).ToList()) c.ParentId = np;
        Items.Remove(t);
        Renumber(np);
        // 노트 group_id=null 처리(있으면 노트 fake와 연동; 없으면 생략)
    }

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
        var oldParentId = self.ParentId;
        self.ParentId = parentId;
        Renumber(parentId);
        if (oldParentId != parentId) Renumber(oldParentId);
    }

    public void ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds)
    {
        // Guard: system group cannot be a new parent.
        if (parentId is int pid0)
        {
            var parentGroup = Items.FirstOrDefault(g => g.Id == pid0);
            if (parentGroup is null || parentGroup.IsSystem) return;
        }

        for (var i = 0; i < orderedGroupIds.Count; i++)
        {
            var id = orderedGroupIds[i];
            var g = Items.First(x => x.Id == id);
            // Self-reference or cycle guard: only update sort_order, not parent_id.
            if (parentId is int pp && (pp == id || IsDescendantOf(pp, id)))
                g.SortOrder = i;
            else
            { g.ParentId = parentId; g.SortOrder = i; }
        }
    }

    private void Renumber(int? parentId)
    {
        var sibs = Items.Where(g => g.ParentId == parentId).OrderBy(g => g.SortOrder).ThenBy(g => g.Id).ToList();
        for (var i = 0; i < sibs.Count; i++) sibs[i].SortOrder = i;
    }
}
