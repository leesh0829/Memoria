using FluentAssertions;
using Memoria.Core;
using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Memoria.Tests;

public class CoreServiceRegistrationTests
{
    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), "memoria_di_" + Guid.NewGuid().ToString("N") + ".db");

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(p)) { try { File.Delete(p); } catch { /* best-effort cleanup */ } }
    }

    [Fact]
    public void AddMemoriaCore_ResolvesAllCoreServices()
    {
        var path = NewDbPath();
        var provider = new ServiceCollection().AddMemoriaCore(path).BuildServiceProvider();
        try
        {
            provider.GetRequiredService<SqliteConnectionFactory>().Should().NotBeNull();
            provider.GetRequiredService<IDatabaseInitializer>().Should().NotBeNull();
            provider.GetRequiredService<IBackupService>().Should().NotBeNull();
            provider.GetRequiredService<IGroupRepository>().Should().NotBeNull();
            provider.GetRequiredService<INoteRepository>().Should().NotBeNull();
            provider.GetRequiredService<IChecklistRepository>().Should().NotBeNull();
            provider.GetRequiredService<IClientRepository>().Should().NotBeNull();
            provider.GetRequiredService<ISettingsRepository>().Should().NotBeNull();
            provider.GetRequiredService<ISearchService>().Should().NotBeNull();
            provider.GetRequiredService<IClientClassifier>().Should().NotBeNull();
            provider.GetRequiredService<IWeekCalculator>().Should().NotBeNull();
            provider.GetRequiredService<IWeeklyReportRenderer>().Should().NotBeNull();
            provider.GetRequiredService<ITaggingService>().Should().NotBeNull();
            provider.GetRequiredService<IWeeklyReportService>().Should().NotBeNull();
        }
        finally { provider.Dispose(); Cleanup(path); }
    }

    [Fact]
    public void AddMemoriaCore_EndToEnd_InitializeAndRenderReport()
    {
        var path = NewDbPath();
        var provider = new ServiceCollection().AddMemoriaCore(path).BuildServiceProvider();
        try
        {
            provider.GetRequiredService<IDatabaseInitializer>().EnsureReady();

            var notes = provider.GetRequiredService<INoteRepository>();
            var items = provider.GetRequiredService<IChecklistRepository>();
            var noteId = notes.Create(new Note { Type = NoteType.Checklist, LogDate = new DateOnly(2026, 6, 23) });
            items.AddItem(new ChecklistItem { NoteId = noteId, Kind = ItemKind.Task, Text = "SLD 점검" });

            var clients = provider.GetRequiredService<IClientRepository>();
            var options = new ReportRenderOptions
            {
                WeekStart = new DateOnly(2026, 6, 22),
                WeekEnd = new DateOnly(2026, 6, 26),
                Clients = clients.GetAll(enabledOnly: true),
            };

            var svc = provider.GetRequiredService<IWeeklyReportService>();
            var result = svc.Build(new DateOnly(2026, 6, 23), options);
            svc.Render(ReportFormatKind.B, result.Data, options)
               .Should().Contain("[ SLD ]\n\t* SLD 점검");
        }
        finally { provider.Dispose(); Cleanup(path); }
    }
}
