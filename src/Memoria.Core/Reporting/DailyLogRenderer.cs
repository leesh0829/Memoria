using System.Collections.Generic;

namespace Memoria.Core.Reporting;

/// <summary>
/// 일일업무일지 Read(양식 출력) 탭의 순수 렌더러.
/// 할일/이슈를 매일 별도 작업 파일에 붙여넣을 번호 목록 문자열로 만든다.
/// 규칙: 리스트 순서 유지, 빈/공백 텍스트는 제외(번호는 필터 후 1부터 연속),
///       텍스트는 그대로 유지(트림 안 함), 줄 구분은 "\n", 마지막 개행 없음, 빈 섹션은 빈 문자열.
/// </summary>
public static class DailyLogRenderer
{
    /// 할일: "{n}. {고객사명} {텍스트}". 고객사가 없거나(=null) 이름을 못 찾거나 공백이면 접두어 생략.
    public static string RenderTasks(
        IReadOnlyList<(string Text, int? ClientId)> tasks,
        IReadOnlyDictionary<int, string> clientNames)
    {
        var lines = new List<string>();
        int n = 0;
        foreach (var (text, clientId) in tasks)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            n++;
            var prefix = "";
            if (clientId is int id
                && clientNames.TryGetValue(id, out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                prefix = name + " ";
            }
            lines.Add($"{n}. {prefix}{text}");
        }
        return string.Join("\n", lines);
    }

    /// 이슈: "{n}. {텍스트}".
    public static string RenderIssues(IReadOnlyList<string> issues)
    {
        var lines = new List<string>();
        int n = 0;
        foreach (var text in issues)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            n++;
            lines.Add($"{n}. {text}");
        }
        return string.Join("\n", lines);
    }
}
