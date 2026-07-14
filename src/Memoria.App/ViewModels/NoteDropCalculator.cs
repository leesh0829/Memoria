namespace Memoria.App.ViewModels;

/// 메모 리스트 순서변경 드롭의 인덱스 계산(순수). GroupDropCalculator의 리스트 버전.
public static class NoteDropCalculator
{
    /// 드롭 대상 항목(targetIndex)의 위/아래(after) 기준으로, 드래그 중인 항목(oldIndex)이
    /// 이동 후 도달해야 할 최종 인덱스를 계산한다.
    /// ObservableCollection.Move(old, new)는 "old에서 제거 후 new에 삽입" 의미이므로,
    /// old가 삽입 지점보다 앞이면 제거로 한 칸 당겨진 것을 보정(-1)한다.
    public static int ResolveInsertIndex(int oldIndex, int targetIndex, bool after)
    {
        int insert = after ? targetIndex + 1 : targetIndex;
        if (oldIndex < insert) insert--;
        return insert;
    }
}
