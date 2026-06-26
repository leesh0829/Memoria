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
        // MATCH 를 메인 쿼리에서 수행해야 FTS5 의 숨은 `rank` 컬럼으로 관련도 정렬이 가능하다.
        // 따라서 notes_fts 에서 MATCH 한 뒤 notes 와 조인해 실 데이터를 읽고 ORDER BY rank 로 정렬한다.
        // 선택 컬럼은 notes(INTEGER/TEXT)와 snippet()(TEXT 스칼라)뿐이라 FTS5 컬럼 타입 모호성(BLOB)을 피한다.
        // snippet() 두번째 인자 -1: 매칭이 가장 잘된 컬럼을 FTS5가 자동 선택한다.
        // rank 는 bm25 점수(좋을수록 더 작은 값)이므로 오름차순(기본) 정렬이 곧 관련도 내림차순이다.
        using var conn = _factory.Open();
        return conn.Query<SearchHitDto>(
            "SELECT n.id AS NoteId, " +
            "       COALESCE(NULLIF(n.title, ''), n.body, '') AS TitlePreview, " +
            "       snippet(notes_fts, -1, '', '', ' … ', 8) AS Snippet " +
            "FROM notes_fts " +
            "JOIN notes n ON n.id = notes_fts.rowid " +
            "WHERE notes_fts MATCH @q " +
            "  AND n.deleted_at IS NULL " +
            "ORDER BY rank;",
            new { q = ftsQuery })
            .Select(r => new SearchHit((int)r.NoteId, r.TitlePreview, r.Snippet ?? ""))
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
