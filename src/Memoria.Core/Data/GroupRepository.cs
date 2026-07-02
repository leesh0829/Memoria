using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class GroupRepository : IGroupRepository
{
    private const string SelectColumns =
        "id AS Id, name AS Name, parent_id AS ParentId, is_system AS IsSystem, " +
        "sort_order AS SortOrder, color AS Color, created_at AS CreatedAt";

    private readonly SqliteConnectionFactory _factory;

    public GroupRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int Create(Group group)
    {
        group.CreatedAt = DateTimeOffset.UtcNow;
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO groups(name, parent_id, is_system, sort_order, color, created_at) " +
                "VALUES(@Name, @ParentId, @IsSystem, @SortOrder, @Color, @CreatedAt);", group);
            group.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return group.Id;
    }

    public void Update(Group group)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE groups SET name = @Name, parent_id = @ParentId, is_system = @IsSystem, " +
                "sort_order = @SortOrder, color = @Color WHERE id = @Id;", group);
        }
    }

    public void Delete(int id)
    {
        var target = Get(id);
        if (target is null) return;
        var newParent = target.ParentId;   // 승격 목적지(조부모 또는 null=루트)
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            // 1) 자식 승격
            conn.Execute("UPDATE groups SET parent_id = @newParent WHERE parent_id = @id;",
                new { newParent, id }, tx);
            // 2) 행 삭제(노트 group_id는 ON DELETE SET NULL)
            conn.Execute("DELETE FROM groups WHERE id = @id;", new { id }, tx);
            // 3) 목적지 형제 재번호(승격된 자식 + 기존 형제)
            var siblings = conn.Query<int>(
                "SELECT id FROM groups WHERE " +
                (newParent is null ? "parent_id IS NULL" : "parent_id = @newParent") +
                " ORDER BY sort_order, id;", new { newParent }, tx).ToList();
            for (var i = 0; i < siblings.Count; i++)
                conn.Execute("UPDATE groups SET sort_order = @i WHERE id = @sid;",
                    new { i, sid = siblings[i] }, tx);
            tx.Commit();
        }
    }

    public Group? Get(int id)
    {
        using var conn = _factory.Open();
        return conn.QuerySingleOrDefault<Group>(
            $"SELECT {SelectColumns} FROM groups WHERE id = @id;", new { id });
    }

    public IReadOnlyList<Group> GetAll()
    {
        using var conn = _factory.Open();
        return conn.Query<Group>(
            $"SELECT {SelectColumns} FROM groups ORDER BY sort_order, id;").ToList();
    }

    public bool IsDescendantOf(int nodeId, int ancestorId)
    {
        // nodeId에서 부모 체인을 따라 올라가며 ancestorId를 만나면 후손.
        var parents = ParentMap();
        var visited = new HashSet<int>();
        var current = nodeId;
        while (parents.TryGetValue(current, out var parent) && parent is int p)
        {
            if (!visited.Add(current)) break;   // 사이클 방어
            if (p == ancestorId) return true;
            current = p;
        }
        return false;
    }

    public void SetParent(int groupId, int? parentId)
    {
        var self = Get(groupId);
        if (self is null || self.IsSystem) return;                       // 없는/시스템 그룹 이동 금지
        if (parentId is int pid)
        {
            if (pid == groupId) return;                                  // 자기 자신
            var parent = Get(pid);
            if (parent is null || parent.IsSystem) return;               // 시스템 부모 금지
            if (IsDescendantOf(pid, groupId)) return;                    // 후손을 부모로 → 사이클
        }

        var oldParentId = self.ParentId;   // 이동 전 소스 부모를 캡처

        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            // 목적지 형제(자기 제외)를 sort_order 순으로 + 자기를 끝에 붙여 재번호.
            var siblings = conn.Query<int>(
                "SELECT id FROM groups WHERE " +
                (parentId is null ? "parent_id IS NULL" : "parent_id = @parentId") +
                " AND id <> @groupId ORDER BY sort_order, id;",
                new { parentId, groupId }, tx).ToList();
            siblings.Add(groupId);
            conn.Execute("UPDATE groups SET parent_id = @parentId WHERE id = @groupId;",
                new { parentId, groupId }, tx);
            for (var i = 0; i < siblings.Count; i++)
                conn.Execute("UPDATE groups SET sort_order = @i WHERE id = @id;",
                    new { i, id = siblings[i] }, tx);
            // 소스 부모가 다를 경우, 소스 형제의 sort_order 갭을 메운다.
            if (oldParentId != parentId)
            {
                var sourceSiblings = conn.Query<int>(
                    "SELECT id FROM groups WHERE " +
                    (oldParentId is null ? "parent_id IS NULL" : "parent_id = @oldParentId") +
                    " ORDER BY sort_order, id;",
                    new { oldParentId }, tx).ToList();
                for (var i = 0; i < sourceSiblings.Count; i++)
                    conn.Execute("UPDATE groups SET sort_order = @i WHERE id = @id;",
                        new { i, id = sourceSiblings[i] }, tx);
            }
            tx.Commit();
        }
    }

    public void ReorderSiblings(int? parentId, IReadOnlyList<int> orderedGroupIds)
    {
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            for (var i = 0; i < orderedGroupIds.Count; i++)
                conn.Execute("UPDATE groups SET sort_order = @i, parent_id = @parentId WHERE id = @id;",
                    new { i, parentId, id = orderedGroupIds[i] }, tx);
            tx.Commit();
        }
    }

    // 모든 그룹의 id -> parent_id 맵(한 번 조회).
    private Dictionary<int, int?> ParentMap()
    {
        using var conn = _factory.Open();
        var rows = conn.Query<(int Id, int? ParentId)>("SELECT id AS Id, parent_id AS ParentId FROM groups;");
        var map = new Dictionary<int, int?>();
        foreach (var r in rows) map[r.Id] = r.ParentId;
        return map;
    }
}
