using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class GroupRepositoryNestingTests
{
    [Fact]
    public void IsDescendantOf_TrueForChildAndGrandchild_FalseForSelfAndUnrelated()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var a = sut.Create(new Group { Name = "A" });
        var b = sut.Create(new Group { Name = "B", ParentId = a });
        var c = sut.Create(new Group { Name = "C", ParentId = b });
        var x = sut.Create(new Group { Name = "X" });

        sut.IsDescendantOf(b, a).Should().BeTrue();   // B는 A의 후손
        sut.IsDescendantOf(c, a).Should().BeTrue();   // C는 A의 후손(손자)
        sut.IsDescendantOf(a, a).Should().BeFalse();  // 자기 자신은 후손 아님
        sut.IsDescendantOf(x, a).Should().BeFalse();  // 무관
    }

    [Fact]
    public void SetParent_RejectsSelf_Descendant_System()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var a = sut.Create(new Group { Name = "A" });
        var b = sut.Create(new Group { Name = "B", ParentId = a });
        var sysId = sut.GetAll().First(g => g.IsSystem).Id;

        sut.SetParent(a, a);        // 자기 자신 → no-op
        sut.Get(a)!.ParentId.Should().BeNull();

        sut.SetParent(a, b);        // 후손(B)을 부모로 → no-op(사이클)
        sut.Get(a)!.ParentId.Should().BeNull();

        sut.SetParent(b, sysId);    // 시스템을 부모로 → no-op
        sut.Get(b)!.ParentId.Should().Be(a);

        sut.SetParent(sysId, a);    // 시스템 이동 → no-op
        sut.Get(sysId)!.ParentId.Should().BeNull();
    }

    [Fact]
    public void SetParent_MovesUnderNewParent_AndRenumbersSiblings()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var p = sut.Create(new Group { Name = "P" });
        var c1 = sut.Create(new Group { Name = "C1", ParentId = p, SortOrder = 0 });
        var c2 = sut.Create(new Group { Name = "C2", ParentId = p, SortOrder = 1 });
        var x = sut.Create(new Group { Name = "X" });

        sut.SetParent(x, p);        // X를 P 하위 끝으로

        sut.Get(x)!.ParentId.Should().Be(p);
        var siblings = sut.GetAll().Where(g => g.ParentId == p).OrderBy(g => g.SortOrder).ToList();
        siblings.Select(g => g.Id).Should().ContainInOrder(c1, c2, x);
        siblings.Select(g => g.SortOrder).Should().ContainInOrder(0, 1, 2); // 0..n 재번호
    }

    [Fact]
    public void Delete_PromotesChildrenToGrandparent_AndRoot()
    {
        using var db = new TestDb();
        var sut = new GroupRepository(db.Factory);
        var gp = sut.Create(new Group { Name = "GP" });
        var p  = sut.Create(new Group { Name = "P",  ParentId = gp });
        var c1 = sut.Create(new Group { Name = "C1", ParentId = p });
        var c2 = sut.Create(new Group { Name = "C2", ParentId = p });

        sut.Delete(p);                                   // P 삭제 → C1,C2가 GP로 승격

        sut.Get(p).Should().BeNull();
        sut.Get(c1)!.ParentId.Should().Be(gp);
        sut.Get(c2)!.ParentId.Should().Be(gp);

        var root = sut.Create(new Group { Name = "Root" });
        var rc = sut.Create(new Group { Name = "RC", ParentId = root });
        sut.Delete(root);                                // 루트 삭제 → 자식이 루트(parent_id=null)
        sut.Get(rc)!.ParentId.Should().BeNull();
    }
}
