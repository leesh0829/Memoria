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
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM groups WHERE id = @id;", new { id });
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
}
