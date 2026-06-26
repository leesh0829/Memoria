using Memoria.Core.Classification;
using Memoria.Core.Data;
using Memoria.Core.Reporting;
using Memoria.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.Core;

public static class CoreServiceRegistration
{
    /// SqliteConnectionFactory(databaseFilePath) + 모든 Repository/Service/Renderer/Classifier/
    /// WeekCalculator/TaggingService/WeeklyReportService/SearchService/IBackupService/IDatabaseInitializer 등록.
    public static IServiceCollection AddMemoriaCore(
        this IServiceCollection services, string databaseFilePath)
    {
        // 단일 영속 쓰기 연결을 공유하려면 팩토리는 싱글턴이어야 한다(계약 §8).
        services.AddSingleton(new SqliteConnectionFactory(databaseFilePath));

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IBackupService>(sp =>
            new BackupService(sp.GetRequiredService<SqliteConnectionFactory>(), databaseFilePath));

        services.AddSingleton<IGroupRepository, GroupRepository>();
        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<IChecklistRepository, ChecklistRepository>();
        services.AddSingleton<IClientRepository, ClientRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ISearchService, SearchService>();

        services.AddSingleton<IClientClassifier, ClientClassifier>();
        services.AddSingleton<IWeekCalculator, WeekCalculator>();
        services.AddSingleton<IWeeklyReportRenderer, WeeklyReportRenderer>();

        services.AddSingleton<ITaggingService, TaggingService>();
        services.AddSingleton<IWeeklyReportService, WeeklyReportService>();

        return services;
    }
}
