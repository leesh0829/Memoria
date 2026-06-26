using System;

namespace Memoria.App.Services;

/// 변경 시점에 캡처한 에디터 내용 스냅샷. 자동저장이 라이브 에디터 상태를 다시 읽어
/// 노트 간 내용이 오염되는 레이스를 막는다(복구 저널과 동일 스냅샷).
public sealed record AutosaveSnapshot(string? Title, string? Body);

public interface IAutosaveService
{
    /// 에디터에서 note를 열 때 저장 콜백 등록. 콜백은 변경 시점 스냅샷을 인자로 받는다.
    void Register(int noteId, Action<AutosaveSnapshot> saveAction);

    /// note를 닫을 때 등록 해제(보류 저장 취소).
    void Unregister(int noteId);

    /// 콘텐츠 변경 알림(변경 시점 스냅샷 포함). debounceMs 만큼 입력이 멈추면
    /// 등록된 save 콜백을 그 스냅샷으로 1회 실행.
    void NotifyChanged(int noteId, AutosaveSnapshot snapshot);

    /// 모든 보류 저장을 즉시 실행(창 종료/숨김/SessionEnding 시).
    void FlushAll();
}
