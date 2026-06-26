using Dapper;
using Memoria.Core.Models;

namespace Memoria.Core.Data;

public sealed class ClientRepository : IClientRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ClientRepository(SqliteConnectionFactory factory) => _factory = factory;

    public int Create(Client client)
    {
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "INSERT INTO clients(name, sort_order, enabled) VALUES(@Name, @SortOrder, @Enabled);", client);
            client.Id = conn.ExecuteScalar<int>("SELECT last_insert_rowid();");
        }
        return client.Id;
    }

    public void Update(Client client)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "UPDATE clients SET name = @Name, sort_order = @SortOrder, enabled = @Enabled WHERE id = @Id;",
                client);
        }
    }

    public void Delete(int id)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute("DELETE FROM clients WHERE id = @id;", new { id });
        }
    }

    public IReadOnlyList<Client> GetAll(bool enabledOnly = false)
    {
        using var conn = _factory.Open();
        var where = enabledOnly ? "WHERE enabled = 1 " : "";
        return conn.Query<Client>(
            "SELECT id AS Id, name AS Name, sort_order AS SortOrder, enabled AS Enabled " +
            $"FROM clients {where}ORDER BY sort_order, id;").ToList();
    }

    public IReadOnlyList<ClientRule> GetRules()
    {
        using var conn = _factory.Open();
        return conn.Query<ClientRule>(
            "SELECT id AS Id, client_id AS ClientId, keyword AS Keyword, priority AS Priority " +
            "FROM client_rules ORDER BY priority, id;").ToList();
    }

    public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules)
    {
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            using var tx = conn.BeginTransaction();
            conn.Execute("DELETE FROM client_rules WHERE client_id = @clientId;",
                new { clientId }, tx);
            foreach (var rule in rules)
            {
                conn.Execute(
                    "INSERT INTO client_rules(client_id, keyword, priority) VALUES(@ClientId, @Keyword, @Priority);",
                    new { ClientId = clientId, rule.Keyword, rule.Priority }, tx);
            }
            tx.Commit();
        }
    }
}
