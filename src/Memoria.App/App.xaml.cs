using System;
using System.Windows;
using Memoria.App.Services;
using Memoria.App.ViewModels;
using Memoria.Core;
using Memoria.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.App;

/// 컴포지션 루트 + 계약 §9.4 부트스트랩 순서의 '기반'(누적 패치 대상).
/// 각 마일스톤(M5/M6/M7/M9)은 기존 호출을 보존하고, 표시된 위치에 자기 배선만 '추가'한다.
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // (1) M2 — 데이터 디렉터리 보장.
        AppPaths.EnsureDirectories();

        // (2) M6 — SingleInstance: 두 번째 인스턴스면 pipe로 인자 전송 후 Shutdown. (M6에서 추가)

        // (3) M2 — DI 합성 + 서비스 로케이터 초기화. (M6/M7이 자기 서비스 등록을 이 블록에 추가)
        var sc = new ServiceCollection();
        sc.AddMemoriaCore(AppPaths.DatabaseFile);   // M1 Core: 초기화/리포지토리/서비스 + 단일 직렬 라이터(busy_timeout=5000)
        sc.AddSingleton<TimeProvider>(TimeProvider.System);
        sc.AddSingleton<IRecoveryJournal>(_ => new RecoveryJournal(AppPaths.RecoveryDirectory));
        sc.AddSingleton<IAutosaveService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsRepository>();
            var ms = int.Parse(settings.GetOrDefault(SettingsKeys.AutosaveDebounceMs, "500"));
            return new DebounceAutosaveService(sp.GetRequiredService<TimeProvider>(), ms);
        });
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();
        AppServices.Initialize(_services);          // 계약 §9.2 — 이후 View/code-behind가 AppServices.Resolve<T>() 사용

        // (4) M2 — DB 준비(파일/PRAGMA/마이그레이션/시드).
        _services.GetRequiredService<IDatabaseInitializer>().EnsureReady();

        // (5) M9 — 무결성 점검 실패 시 최신 백업 복원(+사용자 확인):
        //     if (!_services.GetRequiredService<IBackupService>().IsDatabaseHealthy())
        //         _services.GetRequiredService<IBackupService>().TryRestoreFromLatestBackup();   (M9에서 추가)
        // (6) M9 — 일일 백업:
        //     _services.GetRequiredService<IBackupService>().BackupIfDue(retentionCount);        (M9에서 추가)
        // (7) M7 — _services.GetRequiredService<IThemeService>().Initialize();                   (M7에서 추가)

        var vm = _services.GetRequiredService<MainViewModel>();
        vm.LoadGroups();

        // (8) M2 — 크래시 복구 저널 적용(§8.1): 보류 스냅샷이 있으면 사용자 확인 후 DB에 반영.
        var recovery = _services.GetRequiredService<IRecoveryJournal>();
        var pending = recovery.DetectPending();
        if (pending.Count > 0)
        {
            var answer = MessageBox.Show(
                $"비정상 종료로 저장되지 않은 메모 {pending.Count}건이 있습니다. 복구하시겠습니까?",
                "Memoria 복구", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) vm.ApplyRecovery(pending);
            else foreach (var s in pending) recovery.Clear(s.NoteId);
        }

        // (9) M5 — _services.GetRequiredService<INoteRepository>().PurgeExpiredTrash(trashRetentionDays); (M5에서 추가)

        // (10) M2 — MainWindow 생성/표시. (M6 Tray/Hotkey, M7 SystemThemeSource 구독을 이 위치에 추가)
        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = vm;
        MainWindow = window;

        // (11) M2 — 표시. (M6에서 closeToTray/autostart 정책에 따라 트레이 시작으로 분기)
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 계약 §9.4 OnExit
        _services?.GetService<IAutosaveService>()?.FlushAll();   // (M2) 보류 저장 즉시 확정(§7.7)
        _services?.Dispose();                                    // (M2/M9) SqliteConnectionFactory.Dispose가 PRAGMA wal_checkpoint(TRUNCATE) 후 연결 종료
        // (M6) Tray/Hotkey/Pipe Dispose는 M6에서 추가
        base.OnExit(e);
    }
}
