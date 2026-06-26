using Dapper;
using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class GroupRepositoryTests
{
    [Fact]
    public void Create_Get_RoundTrips()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);

        var id = sut.Create(new Group { Name = "업무", SortOrder = 10, Color = "#FF0000" });
        id.Should().BeGreaterThan(0);

        var loaded = sut.Get(id)!;
        loaded.Name.Should().Be("업무");
        loaded.SortOrder.Should().Be(10);
        loaded.Color.Should().Be("#FF0000");
        loaded.IsSystem.Should().BeFalse();
        loaded.CreatedAt.Should().BeAfter(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void GetAll_IncludesSystemGroups_OrderedBySortOrder()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        sut.Create(new Group { Name = "개인", SortOrder = 99 });

        var all = sut.GetAll();
        all.Should().Contain(g => g.Name == "일일업무일지" && g.IsSystem);
        all.Should().Contain(g => g.Name == "주간보고" && g.IsSystem);
        all.Select(g => g.SortOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Update_PersistsChanges()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var id = sut.Create(new Group { Name = "old" });

        var g = sut.Get(id)!;
        g.Name = "new";
        g.SortOrder = 5;
        sut.Update(g);

        sut.Get(id)!.Name.Should().Be("new");
        sut.Get(id)!.SortOrder.Should().Be(5);
    }

    [Fact]
    public void Delete_SetsNoteGroupIdToNull()
    {
        using var db = new TestDb();
        var groups = new GroupRepository(db.Factory);
        var gid = groups.Create(new Group { Name = "temp" });

        // Seed a note referencing the group via direct SQL; NoteRepository arrives in Task 10.
        var now = DateTimeOffset.UtcNow.ToString("o");
        int nid;
        lock (db.Factory.WriteSync)
        {
            db.Factory.Write.Execute(
                "INSERT INTO notes(group_id, type, title, created_at, updated_at) " +
                "VALUES(@gid, 'plain', 'n', @now, @now);",
                new { gid, now });
            nid = db.Factory.Write.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }

        groups.Delete(gid);

        groups.Get(gid).Should().BeNull();
        using var conn = db.Factory.Open();
        var groupId = conn.ExecuteScalar<int?>(
            "SELECT group_id FROM notes WHERE id = @nid;", new { nid });
        groupId.Should().BeNull();
    }
}
