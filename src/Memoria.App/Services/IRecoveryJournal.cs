using System;
using System.Collections.Generic;

namespace Memoria.App.Services;

/// 편집 중인 미저장 본문 스냅샷. 정상 저장 전 비정상 종료 대비용.
public sealed record RecoverySnapshot(int NoteId, string? Title, string? Body, DateTimeOffset CapturedAt);

public interface IRecoveryJournal
{
    /// 최신 스냅샷을 비동기 저널 기록 대상으로 예약한다. 호출 자체는 파일 I/O를 하지 않는다.
    void Append(RecoverySnapshot snapshot);

    /// 정상 저장 성공 시 해당 note의 저널 파일 삭제.
    void Clear(int noteId);

    /// 시작 시 보류 중인 복구 후보(노트별 최신 스냅샷) 목록.
    IReadOnlyList<RecoverySnapshot> DetectPending();
}
