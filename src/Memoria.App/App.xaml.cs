using System;
using System.Windows;
using System.Windows.Interop;
using Memoria.App.Services;
using Memoria.App.Theming;
using Memoria.App.ViewModels;
using Memoria.App.Views;
using Memoria.App.Windows;
using Memoria.Core;
using Memoria.Core.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Memoria.App;

/// 컴포지션 루트 + 계약 §9.4 부트스트랩 순서의 '기반'(누적 패치 대상).
/// 각 마일스톤(M5/M6/M7/M9)은 기존 호출을 보존하고, 표시된 위치에 자기 배선만 '추가'한다.
public partial class App : Application
{
    private ServiceProvider? _services;

    // M6 — 필드 추가(기존 M2 필드 보존)
    private ISingleInstanceService _singleInstance = null!;
    private IGlobalHotkeyService _hotkey = null!;
    private ITrayService _tray = null!;
    private IAutostartService _autostart = null!;

    // ExitApplication()과 OnExit()가 모두 라이프사이클 서비스를 정리하므로,
    // 정확히 한 번만 Dispose되도록 보장하는 가드.
    private bool _lifecycleServicesDisposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // (1) M2 — 데이터 디렉터리 보장.
        AppPaths.EnsureDirectories();

        // (2) M6 — SingleInstance: 두 번째 인스턴스면 pipe로 인자 전송 후 Shutdown.
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquire())
        {
            ForegroundHelper.AllowAny(); // 첫 인스턴스가 SetForegroundWindow 가능하도록
            _singleInstance.SignalExistingInstance(PipeCommand.NewNote);
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        // 단일 인스턴스 IPC 수신 구독을 '즉시' 연결한다(서버 루프는 TryAcquire에서 이미 시작됨).
        // DI 빌드/DB 초기화 등 나머지 부트스트랩 중에 두 번째 인스턴스가 보내는 신호를 놓치지 않기 위함.
        // BeginInvoke로 디스패처 큐에 넣어, 시작이 끝나 MainWindow가 준비된 뒤 안전하게 실행되도록 한다.
        // (시작 초기에 신호가 와도 MainWindow가 아직 없으면 throw하지 않고 무시한다.)
        _singleInstance.CommandReceived += (_, cmd) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is null) return; // 아직 MainWindow 미생성 — 안전하게 무시
                if (cmd == PipeCommand.NewNote) NewNoteForeground();
                else ShowMainWindow();
            });

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
        // M9 — MainViewModel 은 ISearchService + 하위 에디터 VM 팩토리(Func<>)를 요구한다.
        //      MS.DI 는 Func<T> 를 자동 해석하지 않으므로 명시적 팩토리로 등록한다.
        sc.AddTransient<ChecklistViewModel>();
        sc.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<IGroupRepository>(),
            sp.GetRequiredService<INoteRepository>(),
            sp.GetRequiredService<IAutosaveService>(),
            sp.GetRequiredService<IRecoveryJournal>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ISearchService>(),
            () => sp.GetRequiredService<ChecklistViewModel>(),
            () => sp.GetRequiredService<WeeklyReportViewModel>()));
        sc.AddSingleton<MainWindow>();
        // M4 — WPF 서비스 구현체 + WeeklyReportViewModel 등록.
        sc.AddSingleton<IClipboardService, WpfClipboardService>();
        sc.AddSingleton<IConfirmationDialogService, MessageBoxConfirmationDialogService>();
        // TimeProvider.System 은 위에서 이미 등록됨 → 중복 등록 금지.
        sc.AddTransient<WeeklyReportViewModel>();
        // M5 — 그룹 CRUD 뷰모델 + 휴지통 뷰모델 등록.
        sc.AddTransient<GroupManagementViewModel>();
        sc.AddSingleton<TrashViewModel>();
        // M7 — 테마 서비스 + 설정 창 서비스 + 설정 뷰모델 등록.
        sc.AddSingleton<IThemeApplier, WpfThemeApplier>();
        sc.AddSingleton<ISystemThemeSource, SystemEventsThemeSource>();   // M6 message-only 창과 동일 프로세스 수명
        sc.AddSingleton<IThemeService, ThemeService>();
        sc.AddSingleton<ISettingsWindowService, SettingsWindowService>();
        sc.AddTransient<SettingsViewModel>();                             // 설정 창을 열 때마다 새 인스턴스
        sc.AddTransient<ClientsSettingsViewModel>();
        // M9 — 시작 안전 코디네이터 (무결성 점검 + 복원 + 일일 백업).
        sc.AddSingleton<IStartupSafetyCoordinator, StartupSafetyCoordinator>();
        _services = sc.BuildServiceProvider();
        AppServices.Initialize(_services);          // 계약 §9.2 — 이후 View/code-behind가 AppServices.Resolve<T>() 사용

        // (4) M2 — DB 준비(파일/PRAGMA/마이그레이션/시드).
        _services.GetRequiredService<IDatabaseInitializer>().EnsureReady();

        // (5) + (6) M9 — 무결성 점검 → (손상 시) 최신 백업 복원 → 일일 백업 (계약 §9.4 step5/6).
        {
            var safetySettings = _services.GetRequiredService<ISettingsRepository>();
            var retentionCount = int.Parse(safetySettings.GetOrDefault(SettingsKeys.BackupRetentionCount, "7"));
            var safety = _services.GetRequiredService<IStartupSafetyCoordinator>().Run(retentionCount);
            if (!safety.DatabaseWasHealthy)
            {
                var msg = safety.RestoreSucceeded
                    ? "데이터베이스 손상을 감지하여 최근 정상 백업에서 복원했습니다."
                    : "데이터베이스 손상을 감지했으나 복원할 백업이 없습니다. 손상 파일은 격리되었습니다.";
                MessageBox.Show(msg, "Memoria 데이터 복구",
                    MessageBoxButton.OK,
                    safety.RestoreSucceeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
        // (7) M7 — 저장된 mode/preset/accent를 즉시 적용(시스템 모드 구독은 ThemeService 생성자가 수행).
        AppServices.Resolve<IThemeService>().Initialize();

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

        // (9) M5 — 시작 시 보존기간 만료된 휴지통 항목을 영구삭제 (계약 §9.4 step 9).
        _services.GetRequiredService<TrashViewModel>().PurgeExpiredOnStartup();

        // (10) M6 — MainWindow 생성 + Tray/Hotkey/Autostart 배선.
        var window = _services.GetRequiredService<MainWindow>();
        window.DataContext = vm;
        MainWindow = window;

        var settings = AppServices.Resolve<ISettingsRepository>(); // 계약 §9.2
        var mainWindow = (MainWindow)MainWindow!;                   // M2가 생성·할당한 인스턴스 재사용

        _autostart = new AutostartService();
        _tray = new TrayService();
        _hotkey = new GlobalHotkeyService();

        // 자동시작 설정 동기화
        bool autostartWanted = bool.Parse(settings.GetOrDefault(SettingsKeys.Autostart, "true"));
        if (autostartWanted) _autostart.Enable(); else _autostart.Disable();

        // 트레이
        _tray.Initialize();
        _tray.ToggleRequested += (_, _) => ToggleMainWindow();
        _tray.NewNoteRequested += (_, _) => NewNoteForeground();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.SettingsRequested += (_, _) => mainWindow.ViewModel.OpenSettingsCommand.Execute(null); // 계약 §9.3 (M2 스텁, 본문 M7)
        _tray.ExitRequested += (_, _) => ExitApplication();

        // 전역 단축키
        string hotkeyStr = settings.GetOrDefault(SettingsKeys.HotkeyNewNote, "Ctrl+Alt+N");
        _hotkey.HotkeyPressed += (_, _) => NewNoteForeground();
        _hotkey.Register(hotkeyStr); // 등록 실패 시 false 반환(재지정 UI는 M7)

        // 단일 인스턴스 IPC 수신 구독은 (2)에서 이미 연결됨(구독 누락 레이스 방지).

        // (11) M6 — 표시. (closeToTray/autostart 정책에 따라 트레이 시작으로 분기 — 기본은 Show)
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 계약 §9.4 OnExit
        _services?.GetService<IAutosaveService>()?.FlushAll();                         // (M2) 보류 저장 즉시 확정(§7.7)
        (_services?.GetService<IThemeService>() as IDisposable)?.Dispose();            // (M7) SystemEvents 구독 해제
        _services?.Dispose();                                                           // (M2/M9) SqliteConnectionFactory.Dispose가 PRAGMA wal_checkpoint(TRUNCATE) 후 연결 종료
        DisposeLifecycleServices();                                                     // (M6) Hotkey/Tray/SingleInstance — 정확히 한 번만
        base.OnExit(e);
    }

    // -----------------------------------------------------------------
    // M6 헬퍼 — 포그라운드 메모 생성 / 창 표시 / 토글 / 종료
    // -----------------------------------------------------------------

    private void NewNoteForeground()
    {
        Dispatcher.Invoke(() =>
        {
            ((MainWindow)MainWindow!).ViewModel.NewPlainNoteCommand.Execute(null); // 계약 §9.3
            ShowMainWindow();
        });
    }

    private void ShowMainWindow()
    {
        var mainWindow = (MainWindow)MainWindow!;
        if (!mainWindow.IsVisible) mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
        var handle = new WindowInteropHelper(mainWindow).Handle;
        ForegroundHelper.BringToFront(handle);
    }

    private void ToggleMainWindow()
    {
        var mainWindow = (MainWindow)MainWindow!;
        if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
            mainWindow.Hide();
        else
            ShowMainWindow();
    }

    private void ExitApplication()
    {
        ((MainWindow)MainWindow!).AllowClose = true;
        DisposeLifecycleServices();   // 여기서 한 번 정리 → 이후 Shutdown()이 부르는 OnExit에서는 가드로 재실행 안 함
        Shutdown();
    }

    /// Hotkey/Tray/SingleInstance를 정확히 한 번만 Dispose한다.
    /// ExitApplication()이 먼저 호출되고 Shutdown()→OnExit()가 다시 호출하더라도 이중 Dispose를 방지한다.
    private void DisposeLifecycleServices()
    {
        if (_lifecycleServicesDisposed) return;
        _lifecycleServicesDisposed = true;
        _hotkey?.Dispose();         // (M6) 전역 단축키 해제
        _tray?.Dispose();           // (M6) 트레이 아이콘 제거
        _singleInstance?.Dispose(); // (M6) Mutex/pipe 해제
    }
}
