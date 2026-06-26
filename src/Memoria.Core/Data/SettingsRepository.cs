using Dapper;

namespace Memoria.Core.Data;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SettingsRepository(SqliteConnectionFactory factory) => _factory = factory;

    public string? Get(string key)
    {
        using var conn = _factory.Open();
        return conn.ExecuteScalar<string?>(
            "SELECT value FROM settings WHERE key = @key;", new { key });
    }

    public string GetOrDefault(string key, string fallback) => Get(key) ?? fallback;

    public void Set(string key, string value)
    {
        lock (_factory.WriteSync)
        {
            _factory.Write.Execute(
                "INSERT INTO settings(key, value) VALUES(@key, @value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                new { key, value });
        }
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        using var conn = _factory.Open();
        var rows = conn.Query<(string Key, string Value)>("SELECT key AS Key, value AS Value FROM settings;");
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }
}
