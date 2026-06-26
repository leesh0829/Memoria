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

    [Fact(Skip = "needs Task 10 NoteRepository")]
    public void Delete_SetsNoteGroupIdToNull()
    {
        // Body references NoteRepository which will be added in Task 10.
        // Activate this test (remove Skip) when NoteRepository is implemented.
        throw new NotImplementedException("NoteRepository not yet implemented (Task 10)");
    }
}
