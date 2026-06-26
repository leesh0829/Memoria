using System;

namespace Memoria.App.ViewModels;

/// 에디터 헤더(R5): "생성 yyyy-MM-dd HH:mm · 수정 yyyy-MM-dd HH:mm".
/// 전달된 DateTimeOffset의 자체 시각(offset)을 그대로 포맷한다(VM이 로컬로 변환 후 전달).
public static class EditorHeaderFormatter
{
    public static string Format(DateTimeOffset created, DateTimeOffset updated)
        => $"생성 {created:yyyy-MM-dd HH:mm} · 수정 {updated:yyyy-MM-dd HH:mm}";
}
