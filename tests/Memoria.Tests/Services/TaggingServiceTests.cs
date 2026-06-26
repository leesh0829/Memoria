using FluentAssertions;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Services;
using Memoria.Tests.Data;
using Xunit;

namespace Memoria.Tests.Services;

public class TaggingServiceTests
{
    private static (TaggingService Svc, ClientRepository Clients) Build(TestDb db)
    {
        var clients = new ClientRepository(db.Factory);
        var svc = new TaggingService(new ClientClassifier(), clients);
        return (svc, clients);
    }

    [Fact]
    public void ApplyAutoTag_AutoTask_GetsClassified()
    {
        using var db = new TestDb();
        var (svc, clients) = Build(db);
        var autoFactoryId = clients.GetAll().Single(c => c.Name == "자율형 공장").Id;

        var item = new ChecklistItem { Kind = ItemKind.Task, Text = "자율형공장 점검", IsManual = false };
        var result = svc.ApplyAutoTag(item);

        result.ClientId.Should().Be(autoFactoryId);
    }

    [Fact]
    public void ApplyAutoTag_ManualTask_IsNotOverwritten()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);

        var item = new ChecklistItem { Kind = ItemKind.Task, Text = "SLD 점검", IsManual = true, ClientId = 99 };
        var result = svc.ApplyAutoTag(item);

        result.ClientId.Should().Be(99);
    }

    [Fact]
    public void ApplyAutoTag_Issue_StaysUnclassified()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);

        var item = new ChecklistItem { Kind = ItemKind.Issue, Text = "SLD 장애", IsManual = false };
        var result = svc.ApplyAutoTag(item);

        result.ClientId.Should().BeNull();
    }

    [Fact]
    public void ApplyAutoTag_NoKeyword_ResultsInNull()
    {
        using var db = new TestDb();
        var (svc, _) = Build(db);

        var item = new ChecklistItem { Kind = ItemKind.Task, Text = "기타 잡무", IsManual = false };
        svc.ApplyAutoTag(item).ClientId.Should().BeNull();
    }
}
