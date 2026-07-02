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
}
