using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IGroupRepository
{
    int Create(Group group);
    void Update(Group group);
    void Delete(int id);                  // notes.group_id ON DELETE SET NULL
    Group? Get(int id);
    IReadOnlyList<Group> GetAll();        // 시스템 그룹 포함, SortOrder 정렬
}
