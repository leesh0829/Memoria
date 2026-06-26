using FluentAssertions;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Data;

public class ClientRepositoryTests
{
    [Fact]
    public void GetAll_ReturnsSeededClients_InSortOrder()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);

        var all = sut.GetAll();
        all.Should().HaveCount(6);
        all.Select(c => c.Name).First().Should().Be("SLD");
        all.Select(c => c.SortOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public void GetAll_EnabledOnly_ExcludesDisabled()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);
        var sld = sut.GetAll().Single(c => c.Name == "SLD");
        sld.Enabled = false;
        sut.Update(sld);

        sut.GetAll(enabledOnly: true).Should().NotContain(c => c.Name == "SLD");
        sut.GetAll(enabledOnly: false).Should().Contain(c => c.Name == "SLD");
    }

    [Fact]
    public void GetRules_ReturnsSeededRules()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);

        var rules = sut.GetRules();
        rules.Should().Contain(r => r.Keyword == "자율형공장" && r.Priority == 1);
        rules.Should().Contain(r => r.Keyword == "SLD" && r.Priority == 6);
    }

    [Fact]
    public void Create_AddsClient()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);

        var id = sut.Create(new Client { Name = "신규고객", SortOrder = 7, Enabled = true });
        sut.GetAll().Should().Contain(c => c.Id == id && c.Name == "신규고객");
    }

    [Fact]
    public void ReplaceRules_ReplacesOnlyThatClientRules()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);
        var sld = sut.GetAll().Single(c => c.Name == "SLD");

        sut.ReplaceRules(sld.Id,
        [
            new ClientRule { ClientId = sld.Id, Keyword = "에스엘디", Priority = 6 },
        ]);

        var rules = sut.GetRules();
        rules.Where(r => r.ClientId == sld.Id).Should().ContainSingle()
             .Which.Keyword.Should().Be("에스엘디");
        rules.Should().Contain(r => r.Keyword == "자율형공장"); // 다른 고객사 규칙은 유지
    }

    [Fact]
    public void Delete_RemovesClientAndCascadesRules()
    {
        using var db = new TestDb();
        var sut = new ClientRepository(db.Factory);
        var carbon = sut.GetAll().Single(c => c.Name == "카본센스");

        sut.Delete(carbon.Id);

        sut.GetAll().Should().NotContain(c => c.Id == carbon.Id);
        sut.GetRules().Should().NotContain(r => r.ClientId == carbon.Id);
    }
}
