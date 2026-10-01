namespace Memoria.Core.Data;

public interface ISettingsRepository
{
    string? Get(string key);
    string GetOrDefault(string key, string fallback);
    void Set(string key, string value);
    void SetMany(IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
            Set(pair.Key, pair.Value);
    }
    IReadOnlyDictionary<string, string> GetAll();
}
