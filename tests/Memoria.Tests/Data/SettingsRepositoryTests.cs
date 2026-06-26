using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Data;
using Xunit;

namespace Memoria.Tests.Data;

public class SettingsRepositoryTests
{
    [Fact]
    public void Get_ReturnsSeededValue_AndNullForMissing()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        sut.Get(SettingsKeys.ReporterName).Should().Be("이승현");
        sut.Get("does.not.exist").Should().BeNull();
    }

    [Fact]
    public void GetOrDefault_FallsBack_WhenMissing()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        sut.GetOrDefault("does.not.exist", "fallback").Should().Be("fallback");
        sut.GetOrDefault(SettingsKeys.ThemeMode, "x").Should().Be("system");
    }

    [Fact]
    public void Set_InsertsAndUpdates()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        sut.Set("custom.key", "v1");
        sut.Get("custom.key").Should().Be("v1");

        sut.Set("custom.key", "v2");
        sut.Get("custom.key").Should().Be("v2");
    }

    [Fact]
    public void GetAll_ContainsSeededKeys()
    {
        using var db = new TestDb();
        var sut = new SettingsRepository(db.Factory);

        var all = sut.GetAll();
        all.Should().ContainKey(SettingsKeys.Autostart);
        all[SettingsKeys.Autostart].Should().Be("true");
    }
}
