using Dapper;
using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.Tests.Data;

public class WorkEntryRepositoryTests
{
    [Fact]
    public void SaveReloadUpdateDelete_PreservesDatesMultipleSelectionsAndNarrative()
    {
        using var db = new TestDb();
        var repository = new WorkEntryRepository(db.Factory);
        var entry = new WorkEntry
        {
            Statuses = ["검토 중", "회의필요"], Clients = ["SLD_파주", "SLD_공주"],
            RequestTypes = ["MES", "프로그램개발"], WorkTypes = ["개발", "설계"], RequestMethods = ["유선", "이메일"],
            RequestedDate = new(2026, 10, 16), ProcessingStart = new(2026, 10, 19), ProcessingEnd = new(2026, 10, 22),
            Request = "첫 줄\n두번째 줄", Cause = "원인", Action = "조치", Requester = "고객", Assignee = "담당자",
        };
        repository.Save(entry);
        new WorkEntryRepository(db.Factory).GetAll().Should().ContainSingle().Which.Should().BeEquivalentTo(entry);
        entry.Done = true;
        entry.Action = "처리 완료";
        repository.Save(entry);
        repository.GetAll().Should().ContainSingle().Which.Action.Should().Be("처리 완료");
        repository.Delete(entry.Id);
        repository.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void V6Migration_PreservesOldReportBodyAndClientRules_AndIsIdempotent()
    {
        using var db = new TestDb();
        var notes = new NoteRepository(db.Factory);
        var id = notes.Create(new Note { Type = NoteType.WeeklyReport, ReportFormat = ReportFormatKind.C, Body = "기존 C 편집본" });
        var connection = db.Factory.Write;
        connection.Execute("UPDATE clients SET name = '충북테크놀로지파크' WHERE name = '충북테크노파크 (FEMS)';");
        connection.Execute("DROP TABLE work_entries; DELETE FROM _migrations WHERE version >= 6; PRAGMA user_version = 5;");
        var initializer = new DatabaseInitializer(db.Factory);
        initializer.EnsureReady();
        initializer.EnsureReady();
        notes.Get(id)!.ReportFormat.Should().Be(ReportFormatKind.D);
        notes.Get(id)!.Body.Should().Be("기존 C 편집본");
        new SettingsRepository(db.Factory).Get(SettingsKeys.ShowFormatD).Should().Be("false");
        connection.ExecuteScalar<int>("SELECT COUNT(*) FROM client_rules r JOIN clients c ON c.id=r.client_id WHERE c.name='충북테크노파크 (FEMS)';").Should().BeGreaterThan(0);
        new WorkEntryRepository(db.Factory).GetAll().Should().BeEmpty();
    }

    [Theory]
    [InlineData("완료 (테스트 필요)")]
    [InlineData("완료(테스트 필요)")]
    public void V8Migration_ReplacesCompletedTestStatus_AndSetsDoneWithoutChangingOtherData(string status)
    {
        using var db = new TestDb();
        var repository = new WorkEntryRepository(db.Factory);
        var entry = new WorkEntry { Statuses = [status], Request = "내용 유지", Action = "조치 유지" };
        var untouched = new WorkEntry { Statuses = ["보류"], Request = "다른 항목" };
        repository.Save(entry);
        repository.Save(untouched);
        lock (db.Factory.WriteSync)
            db.Factory.Write.Execute("DELETE FROM _migrations WHERE version = 8; PRAGMA user_version = 7;");
        var initializer = new DatabaseInitializer(db.Factory);
        initializer.EnsureReady();
        initializer.EnsureReady();
        var entries = repository.GetAll();
        entries.Should().HaveCount(2);
        var changed = entries.Single(e => e.Id == entry.Id);
        changed.Statuses.Should().Equal("완료");
        changed.Done.Should().BeTrue();
        changed.Request.Should().Be("내용 유지");
        changed.Action.Should().Be("조치 유지");
        entries.Single(e => e.Id == untouched.Id).Should().BeEquivalentTo(untouched);
    }

}
