using Memoria.Core.Models;

namespace Memoria.Core.Data;

public interface IClientRepository
{
    int Create(Client client);
    void Update(Client client);
    void Delete(int id);                                       // checklist_items.client_id ON DELETE SET NULL
    IReadOnlyList<Client> GetAll(bool enabledOnly = false);    // SortOrder 정렬
    IReadOnlyList<ClientRule> GetRules();                      // 전체 규칙
    void ReplaceRules(int clientId, IEnumerable<ClientRule> rules);
}
