using Memoria.Core.Data;

namespace Memoria.Tests.Fakes;

public sealed class FakeSettingsRepository : ISettingsRepository
{
    public readonly Dictionary<string, string> Store = new();

    public string? Get(string key) => Store.TryGetValue(key, out var v) ? v : null;

    public string GetOrDefault(string key, string fallback) =>
        Store.TryGetValue(key, out var v) ? v : fallback;

    public void Set(string key, string value) => Store[key] = value;

    public IReadOnlyDictionary<string, string> GetAll() => Store;
}
