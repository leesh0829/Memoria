using System.Globalization;
using Memoria.Core.Models;

namespace Memoria.Core.Reporting;

/// 양식 C에 포함할 고객사 선택. 설정에는 client id를 쉼표로 이어 저장한다(이름은 개명될 수 있어 id를 쓴다).
public static class FormatCClients
{
    /// 설정이 아직 없을 때 기본으로 선택되는 고객사.
    public const string DefaultClientName = "SLD 자율형공장";

    /// 저장값 + 현재 고객사 목록 → 포함할 client id 목록.
    /// null 반환 = 필터 없음(모든 업무 포함). 빈 목록 반환 = 사용자가 모두 해제(업무 없음).
    public static IReadOnlyList<int>? Resolve(string? stored, IEnumerable<Client> clients)
    {
        var all = clients as IReadOnlyList<Client> ?? clients.ToList();

        if (stored is null)
        {
            // 미설정 → 기본 고객사 이름으로 찾는다. 그 이름이 없으면(개명/삭제) 필터를 걸지 않는다.
            var defaults = all.Where(c => c.Name == DefaultClientName).Select(c => c.Id).ToList();
            return defaults.Count > 0 ? defaults : null;
        }

        var known = all.Select(c => c.Id).ToHashSet();
        return stored
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : (int?)null)
            .Where(id => id is int v && known.Contains(v))
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }

    public static string Format(IEnumerable<int> clientIds) =>
        string.Join(",", clientIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
}
