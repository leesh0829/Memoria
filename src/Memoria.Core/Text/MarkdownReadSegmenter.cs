using System.Text.RegularExpressions;

namespace Memoria.Core.Text;

/// <summary>읽기 뷰용 세그먼트. IsImage면 Value=이미지 상대경로, 아니면 Value=리터럴 텍스트.</summary>
public sealed record ReadSegment(bool IsImage, string Value);

/// <summary>본문을 이미지 참조 `![alt](path)` 기준으로 텍스트/이미지 세그먼트로 분할(순수).</summary>
public static class MarkdownReadSegmenter
{
    private static readonly Regex ImgRx = new(@"!\[[^\]]*\]\(([^)]+)\)", RegexOptions.Compiled);

    public static IReadOnlyList<ReadSegment> Segment(string? body)
    {
        var result = new List<ReadSegment>();
        if (string.IsNullOrEmpty(body)) return result;
        int pos = 0;
        foreach (Match m in ImgRx.Matches(body))
        {
            if (m.Index > pos)
                result.Add(new ReadSegment(false, body.Substring(pos, m.Index - pos)));
            result.Add(new ReadSegment(true, m.Groups[1].Value.Trim()));
            pos = m.Index + m.Length;
        }
        if (pos < body.Length)
            result.Add(new ReadSegment(false, body.Substring(pos)));
        return result;
    }
}
