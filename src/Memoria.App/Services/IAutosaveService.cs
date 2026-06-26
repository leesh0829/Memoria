using System;

namespace Memoria.App.Services;

public interface IAutosaveService
{
    /// 에디터에서 note를 열 때 저장 콜백 등록.
    void Register(int noteId, Action saveAction);

    /// note를 닫을 때 등록 해제(보류 저장 취소).
    void Unregister(int noteId);

    /// 콘텐츠 변경 알림. debounceMs 만큼 입력이 멈추면 등록된 save 콜백 1회 실행.
    void NotifyChanged(int noteId);

    /// 모든 보류 저장을 즉시 실행(창 종료/숨김/SessionEnding 시).
    void FlushAll();
}
