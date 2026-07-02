using FluentAssertions;
using Memoria.App.ViewModels;
using Xunit;

namespace Memoria.Tests.App;

public class GroupDropCalculatorTests
{
    [Theory]
    [InlineData(2, 100, DropZone.Before)]
    [InlineData(50, 100, DropZone.Into)]
    [InlineData(90, 100, DropZone.After)]
    public void ZoneForOffset_MapsThreeZones(double y, double h, DropZone expected)
        => GroupDropCalculator.ZoneForOffset(y, h).Should().Be(expected);

    [Fact]
    public void Resolve_Into_TargetsGroupAsParent()
    {
        var (parent, index) = GroupDropCalculator.Resolve(DropZone.Into, targetGroupId: 5, targetParentId: 1, targetIndexAmongSiblings: 2);
        parent.Should().Be(5);
        index.Should().Be(int.MaxValue);
    }

    [Fact]
    public void Resolve_BeforeAfter_UseTargetParentAndIndex()
    {
        GroupDropCalculator.Resolve(DropZone.Before, 5, 1, 2).Should().Be(((int?)1, 2));
        GroupDropCalculator.Resolve(DropZone.After, 5, 1, 2).Should().Be(((int?)1, 3));
    }
}
