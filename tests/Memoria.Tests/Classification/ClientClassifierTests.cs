using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.Classification;

public class ClientClassifierTests
{
    // 고객사 id: SLD=1, MTP=2, 코모텍=3, 충북=4, 자율형 공장=5, 카본센스=6
    // 시드 진실원본(DatabaseInitializer.cs SeedRulesSql)과 일치
    private static readonly List<ClientRule> Rules =
    [
        new() { ClientId = 5, Keyword = "자율형공장", Priority = 1 },
        new() { ClientId = 5, Keyword = "자율형 공장", Priority = 1 },
        new() { ClientId = 4, Keyword = "충북", Priority = 2 },
        new() { ClientId = 4, Keyword = "충북테크놀로지파크", Priority = 2 },
        new() { ClientId = 4, Keyword = "DL정보기술", Priority = 2 },
        new() { ClientId = 3, Keyword = "코모텍", Priority = 3 },
        new() { ClientId = 2, Keyword = "MTP", Priority = 4 },
        new() { ClientId = 2, Keyword = "머티리얼즈파크", Priority = 4 },
        new() { ClientId = 6, Keyword = "카본센스", Priority = 5 },
        new() { ClientId = 1, Keyword = "SLD", Priority = 6 },
    ];

    private static readonly HashSet<int> AllEnabled = [1, 2, 3, 4, 5, 6];
    private readonly IClientClassifier _sut = new ClientClassifier();

    [Fact]
    public void AutonomousFactory_BeatsSld_WhenBothPresent()
    {
        _sut.Classify("SLD 자율형공장 정리", Rules, AllEnabled).Should().Be(5);
    }

    [Fact]
    public void Sld_WhenOnlySldPresent()
    {
        _sut.Classify("SLD 점검", Rules, AllEnabled).Should().Be(1);
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        _sut.Classify("mtp 라인 작업", Rules, AllEnabled).Should().Be(2);
    }

    [Fact]
    public void NoKeyword_ReturnsNull()
    {
        _sut.Classify("기타 잡무 정리", Rules, AllEnabled).Should().BeNull();
    }

    [Fact]
    public void DisabledClientRules_AreIgnored()
    {
        var enabledWithoutSld = new HashSet<int> { 2, 3, 4, 5, 6 };
        _sut.Classify("SLD 점검", Rules, enabledWithoutSld).Should().BeNull();
    }

    [Fact]
    public void ChungbukKeywordVariants_Match()
    {
        _sut.Classify("DL정보기술 협의", Rules, AllEnabled).Should().Be(4);
    }

    // 골든 케이스: 시드(DatabaseInitializer.cs SeedRulesSql) 기반
    [Fact]
    public void MaterialsPark_MapsMtp_ClientId2()
    {
        _sut.Classify("머티리얼즈파크 현장 방문", Rules, AllEnabled).Should().Be(2);
    }

    [Fact]
    public void CarbonSense_MapsClientId6()
    {
        _sut.Classify("카본센스 장비 점검", Rules, AllEnabled).Should().Be(6);
    }

    [Fact]
    public void AutonomousFactoryWithSpace_MapsClientId5()
    {
        _sut.Classify("자율형 공장 현장 점검", Rules, AllEnabled).Should().Be(5);
    }

    [Fact]
    public void ChungbukTechParkFullKeyword_MapsClientId4()
    {
        _sut.Classify("충북테크놀로지파크 협의", Rules, AllEnabled).Should().Be(4);
    }
}
