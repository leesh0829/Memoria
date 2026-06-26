using Dapper;

namespace Memoria.Core.Data;

public sealed class SearchService : ISearchService
{
    private readonly SqliteConnectionFactory _factory;

    public SearchService(SqliteConnectionFactory factory) => _factory = factory;

    public IReadOnlyList<SearchHit> Search(string query)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0) return [];

        // 사용자 입력을 FTS5 구문 오류 없이 안전하게 평가하기 위해 전체를 인용된 구(phrase)로 감싼다.
        var ftsQuery = "\"" + trimmed.Replace("\"", "\"\"") + "\"";

        // SearchHitDto(mutable class)로 매핑: Dapper가 SQLite INTEGER(→Int64)와 TEXT 컬럼을
        // 프로퍼티 기반으로 안전하게 변환한 뒤 SearchHit(record)로 투영한다.
        // FTS5 가상 테이블 컬럼 타입 모호성(BLOB 리포팅)을 피하기 위해
        // FTS5를 서브쿼리로 격리하고 실 데이터는 notes 테이블에서 읽는다.
        using var conn = _factory.Open();
        return conn.Query<SearchHitDto>(
            "SELECT n.id AS NoteId, " +
            "       COALESCE(NULLIF(n.title, ''), n.body, '') AS TitlePreview, " +
            "       '' AS Snippet " +
            "FROM notes n " +
            "WHERE n.deleted_at IS NULL " +
            "  AND n.id IN (SELECT rowid FROM notes_fts WHERE notes_fts MATCH @q);",
            new { q = ftsQuery })
            .Select(r => new SearchHit((int)r.NoteId, r.TitlePreview, r.Snippet))
            .ToList();
    }

    // Dapper 역직렬화용 private DTO.
    // positional record SearchHit은 생성자 파라미터 타입이 int인데 SQLite INTEGER는 Int64를 반환하므로
    // 프로퍼티 기반 매핑(mutable class)을 사용해 타입 강제 변환을 Dapper에 위임한다.
    private sealed class SearchHitDto
    {
        public long NoteId { get; set; }
        public string TitlePreview { get; set; } = "";
        public string Snippet { get; set; } = "";
    }
}
