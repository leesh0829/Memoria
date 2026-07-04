using System.Threading;
using System.Threading.Tasks;

namespace Memoria.Core.Sheets;

/// <summary>스프레드시트 셀 격자를 읽는 계약(구글 의존성 없음). 실패 시 예외.</summary>
public interface ISpreadsheetReader
{
    Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(
        string sheetId, string tabName, CancellationToken ct = default);
}
