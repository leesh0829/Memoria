using FluentAssertions;
using Memoria.App.ViewModels;
using Xunit;

namespace Memoria.Tests.App;

public class NoteDropCalculatorTests
{
    // 리스트 [A(0), B(1), C(2)] 기준. Move(old, new) 후 도달할 최종 인덱스를 검증한다.

    [Fact]
    public void DragFirst_DropAfterLast_GoesToEnd()
        => NoteDropCalculator.ResolveInsertIndex(oldIndex: 0, targetIndex: 2, after: true).Should().Be(2);

    [Fact]
    public void DragFirst_DropBeforeLast_LandsJustBeforeIt()
        => NoteDropCalculator.ResolveInsertIndex(oldIndex: 0, targetIndex: 2, after: false).Should().Be(1);

    [Fact]
    public void DragLast_DropBeforeFirst_GoesToTop()
        => NoteDropCalculator.ResolveInsertIndex(oldIndex: 2, targetIndex: 0, after: false).Should().Be(0);

    [Fact]
    public void DragMiddle_DropAfterLast_GoesToEnd()
        => NoteDropCalculator.ResolveInsertIndex(oldIndex: 1, targetIndex: 2, after: true).Should().Be(2);

    [Fact]
    public void DropBeforeItself_IsNoOp_SameIndex()
        => NoteDropCalculator.ResolveInsertIndex(oldIndex: 1, targetIndex: 1, after: false).Should().Be(1);

    [Fact]
    public void DropAfterItself_IsNoOp_SameIndex()
        => NoteDropCalculator.ResolveInsertIndex(oldIndex: 0, targetIndex: 0, after: true).Should().Be(0);
}
