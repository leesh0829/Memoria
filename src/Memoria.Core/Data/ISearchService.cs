namespace Memoria.Core.Data;

public sealed record SearchHit(int NoteId, string TitlePreview, string Snippet);

public interface ISearchService
{
    /// FTS5로 title+body+items 검색. 빈 쿼리는 빈 결과.
    IReadOnlyList<SearchHit> Search(string query);
}
