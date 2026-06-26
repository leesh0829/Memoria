using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IChecklistRepository
{
    int AddItem(ChecklistItem item);
    void UpdateItem(ChecklistItem item);
    void DeleteItem(int id);
    IReadOnlyList<ChecklistItem> GetByNote(int noteId);  // SortOrder 정렬
}
