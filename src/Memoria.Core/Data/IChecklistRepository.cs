using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IChecklistRepository
{
    int AddItem(ChecklistItem item);
    void UpdateItem(ChecklistItem item);
    void UpdateItems(IReadOnlyList<ChecklistItem> items);
    void UpdateItemMetadata(ChecklistItem item);
    void SetSortOrders(IReadOnlyList<ChecklistItem> items);
    void DeleteItem(int id);
    IReadOnlyList<ChecklistItem> GetByNote(int noteId);  // SortOrder 정렬
    IReadOnlyList<ChecklistItem> GetByNotes(IReadOnlyList<int> noteIds);
}
