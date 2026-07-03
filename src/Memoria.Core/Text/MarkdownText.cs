using System.Text.RegularExpressions;

namespace Memoria.Core.Text;

/// <summary>제목 표시용으로 마크다운 마커를 제거한다(렌더링용 아님).</summary>
public static class MarkdownText
{
    public static string StripMarkers(string line)
    {
        var s = line.Trim();
        s = Regex.Replace(s, @"^#{1,6}\s+", "");        // 제목
        s = Regex.Replace(s, @"^>\s+", "");              // 인용
        s = Regex.Replace(s, @"^([-*+]|\d+\.)\s+", "");  // 목록(글머리/번호)
        s = s.Replace("**", "").Replace("__", "");       // 굵게
        s = s.Replace("*", "").Replace("`", "");         // 기울임/코드
        return s.Trim();
    }
}
