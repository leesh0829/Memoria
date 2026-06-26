namespace Memoria.Core.Data;

public interface ISettingsRepository
{
    string? Get(string key);
    string GetOrDefault(string key, string fallback);
    void Set(string key, string value);
    IReadOnlyDictionary<string, string> GetAll();
}
