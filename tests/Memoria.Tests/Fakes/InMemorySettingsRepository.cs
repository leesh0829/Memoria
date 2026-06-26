// tests/Memoria.Tests/Fakes/InMemorySettingsRepository.cs
using System.Collections.Generic;
using Memoria.Core.Data;

namespace Memoria.Tests.Fakes;

public sealed class InMemorySettingsRepository : ISettingsRepository
{
    private readonly Dictionary<string, string> _store = new();

    public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;

    public string GetOrDefault(string key, string fallback)
        => _store.TryGetValue(key, out var v) ? v : fallback;

    public void Set(string key, string value) => _store[key] = value;

    public IReadOnlyDictionary<string, string> GetAll() => _store;
}
