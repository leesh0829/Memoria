using FluentAssertions;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Xunit;

namespace Memoria.Tests.Reporting;

public class FormatCClientsTests
{
    private static IReadOnlyList<Client> Clients() =>
    [
        new() { Id = 1, Name = "SLD", SortOrder = 1, Enabled = true },
        new() { Id = 5, Name = "SLD 자율형공장", SortOrder = 5, Enabled = true },
        new() { Id = 6, Name = "카본센스", SortOrder = 6, Enabled = true },
    ];

    [Fact]
    public void Unset_DefaultsToSldAutonomousFactory()
    {
        FormatCClients.Resolve(null, Clients()).Should().Equal(5);
    }

    [Fact]
    public void Unset_FallsBackToNoFilter_WhenDefaultClientMissing()
    {
        // 기본 고객사가 개명/삭제된 DB → 필터 없이 전부 출력(빈 보고서가 나오는 것보다 낫다).
        IReadOnlyList<Client> renamed = [new() { Id = 5, Name = "자율형 공장", SortOrder = 5, Enabled = true }];

        FormatCClients.Resolve(null, renamed).Should().BeNull();
    }

    [Fact]
    public void Stored_ParsesCommaSeparatedIds()
    {
        FormatCClients.Resolve("1,5", Clients()).Should().Equal(1, 5);
    }

    [Fact]
    public void Stored_EmptyString_MeansNoClientSelected()
    {
        FormatCClients.Resolve("", Clients()).Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Stored_DropsUnknownAndMalformedIds()
    {
        FormatCClients.Resolve("5, 99, abc, 5", Clients()).Should().Equal(5);
    }

    [Fact]
    public void Format_JoinsIdsWithComma()
    {
        FormatCClients.Format([1, 5]).Should().Be("1,5");
        FormatCClients.Format([]).Should().Be("");
    }
}
