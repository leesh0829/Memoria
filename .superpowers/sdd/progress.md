# Memoria SDD Progress Ledger

Branch: impl/memoria
Plans: docs/superpowers/plans/2026-06-26-memoria-m{1..9}-*.md (+ interface-contracts)
Build/test: Windows dotnet.exe via WSL interop (validated).

## Status
(append "Task <id>: complete (commits base7..head7, review clean)" as tasks finish)

### M1 — Core 엔진 (16 tasks) — fully unit-testable here
### M2 — WPF 셸 (9) — compile-verify here, manual on Windows
### M3 — 체크리스트 (9)
### M4 — 주간보고 뷰 (4)
### M5 — 그룹·휴지통 (12)
### M6 — Windows 통합 (7)
### M7 — 테마·설정 (8)
### M8 — 문서·CI·릴리스 (8)
### M9 — 셸 통합·데이터안전 (6)

## Log
Task 1: implemented head 0318d36 | dotnet test: 3 passed, 0 failed
Task 2: implemented head 4bd0fa1 | dotnet test: 4 passed, 0 failed
Task 3: implemented head f7d65b2 | dotnet test: 13 passed, 0 failed
Task 4: implemented head 78c6f3c | dotnet test: 15 passed, 0 failed
Task 5: implemented head acc9031 | dotnet test: 17 passed, 0 failed
Task 6: implemented head 330c4a4 | dotnet test: 20 passed, 0 failed

--- M1 chunk 1 (tasks 1-6) — VERIFIED 20/20 tests green @330c4a4 ---
Task 1: complete (commits 0603284..53364a3, review clean) [solution scaffold + models]
Task 2: complete (head 4bd0fa1, review clean) [WeekCalculator]
Task 3: complete (head f7d65b2, review clean) [ClientClassifier]
Task 4: complete (head 78c6f3c, review clean) [report types + Format A]
Task 5: complete (head acc9031, review clean) [Format B]
Task 6: complete (head 330c4a4, review clean) [SQLite schema/migration/seed/FTS5]

Task 7: implemented head 529c53b | dotnet test: 24 passed, 0 failed
Task 8: implemented head 05c2d64 | dotnet test: 27 passed, 0 failed (1 skipped: Delete_SetsNoteGroupIdToNull pending Task 10 NoteRepository)
Task 9: implemented head 6991594 | dotnet test: 34 passed, 0 failed

Task 10: implemented head 0b0d9c3 | dotnet test: 40 passed, 0 failed

Task 11: implemented head 3bcf81a | dotnet test: 44 passed, 0 failed

MINOR findings (defer to final review):
- T1: redundant `using Xunit;` in ModelsTests.cs:3 (csproj has global Using)
- T3: ClientClassifierTests AllEnabled fixture mutable HashSet -> consider IReadOnlySet/FrozenSet
- T4: IWeeklyReportRenderer.cs Render XML doc shortened vs contract §3
- T6: DapperConfig.EnsureRegistered() tiny race window -> Lazy<bool>

--- M1 chunk 2 (tasks 7-11) — VERIFIED 44/44 green ---
Task 7: complete (head 529c53b, review clean) [SettingsRepository]
Task 8: complete (head 2af7e65, review clean) [GroupRepository]
Task 9: complete (head 6991594, review clean) [ClientRepository]
Task 10: complete (head 0b0d9c3, review clean) [NoteRepository]
Task 11: complete (head 3bcf81a, review clean) [ChecklistRepository]
MINOR (defer): T8 group-delete test uses raw SQL; T10 INoteRepository.Update doc trimmed; T11 UpdateItem caller-policy timestamp + dead var

Task 12: implemented head e76ebf1 | dotnet test: 48 passed, 0 failed
Task 13: implemented head e0894e5 | dotnet test: 53 passed, 0 failed
Task 14: implemented head 5404b16 | dotnet test: 57 passed, 0 failed
Task 15: implemented head e0f3918 | dotnet test: 62 passed, 0 failed
Task 16: implemented head a5baa87 | dotnet test: 64 passed, 0 failed

--- M1 chunk 3 (tasks 12-16) + fixes — VERIFIED 65/65 green (4x stable) ---
Task 12: complete (head 16c045c, +fix 234a659 ORDER BY rank) [SearchService FTS5]
Task 13: complete (head e0894e5, review clean) [TaggingService]
Task 14: complete (head 5404b16, review clean) [WeeklyReportService]
Task 15: complete (head e0f3918, +fix b338f88 path/lock/.corrupt) [BackupService]
Task 16: complete (head a5baa87, review clean) [AddMemoriaCore DI]
*** M1 COMPLETE: 65/65 tests green, full Core engine. HEAD now b338f88. ***
WATCH: one-off xunit flake (TaggingServiceTests) under heavy parallel load; tests use unique GUID temp paths (isolation OK). Revisit in M8/CI if it recurs.

NEXT: M2 (WPF shell) — build headlessly verified OK via dotnet.exe.
M2 Task 1: head 86cb9e0 | dotnet test: 67 passed (65 existing + 2 new AppPaths), 0 failed
M2 Task 2: head 121ca72 | dotnet test: 70 passed (67 existing + 3 new RecoveryJournal), 0 failed
M2 Task 3: head 936d9e4 | dotnet test: 74 passed (70 existing + 4 new DebounceAutosave), 0 failed
M2 Task 4: head 2c00d5f | dotnet test: 75 passed (74 existing + 1 new MainViewModelSidebar), 0 failed
M2 Task 5: head d241f92 | dotnet test: 81 passed (75 existing + 6 new NoteListItem/NoteTitleResolver/MainViewModelNotes), 0 failed

--- M2 chunk 1 (tasks 1-5) + fixes — VERIFIED 85/85 green ---
M2 Task 1: complete (head 86cb9e0) [App scaffold + AppPaths]
M2 Task 2: complete (head 121ca72, +fix 7dfae3f JsonException) [RecoveryJournal]
M2 Task 3: complete (head 936d9e4, +fix 2158212 timer race) [DebounceAutosave]
M2 Task 4: complete (head 2c00d5f) [sidebar group tree]
M2 Task 5: complete (head aa9df2a) [note list + title rule + new note]
MINOR (defer): unused template usings in App.xaml.cs/MainWindow.xaml.cs (MainWindow replaced in T9); RecoveryJournalTests temp dir cleanup
M2 Task 6: head 6748571 | dotnet test: 90 passed (85 existing + 5 new EditorHeader/MainViewModelEditor), 0 failed
M2 Task 7: head eae367a | dotnet test: 93 passed (90 existing + 3 new MainViewModelStubCommands), 0 failed
M2 Task 8: head f1941ac | dotnet test: 94 passed (93 existing + 1 new AppServicesTests), 0 failed
M2 Task 9: head 7c50d41 | dotnet test: 94 passed, 0 failed (DI composition root + MainWindow shell + startup recovery wiring; manual MV-1~MV-8 pending Windows run)

--- M2 chunk 2 (tasks 6-9) + post-fix — VERIFIED 94/94 green ---
M2 Task 6: complete (head 6748571) [plain editor + autosave/recovery wiring + header]
M2 Task 7: complete (head eae367a) [MainViewModel stub commands + SelectedNote/CurrentNoteType]
M2 Task 8: complete (head be24a15) [AppServices locator]
M2 Task 9: complete (head 7c50d41) [DI composition root + MainWindow + bootstrap §9.4]
post-fix: 17 brush keys complete + AppServices hardened
*** M2 COMPLETE: 94/94 tests, App is runnable (App.xaml.cs bootstrap wired). ***
MINOR (defer to final review): unused template usings; SaveCurrent bg-thread reads VM props (safe now); AppServices Reset test-only

M3 Task 1: head f35348d | dotnet test: 101 passed (94 existing + 7 new ChecklistItemViewModel), 0 failed
M3 Task 2: head b2ccd5c | dotnet test: 103 passed (101 existing + 2 new ChecklistViewModel Load), 0 failed
M3 Task 3: head ad19053 | dotnet test: 108 passed (103 existing + 5 new AddTask/AddIssue/SortOrder/RemoveItem/TouchNote), 0 failed
M3 Task 4: head 29767cf | dotnet test: 112 passed (108 existing + 4 new ToggleDone), 0 failed
M3 Task 5: head 7611f48 | dotnet test: 116 passed (112 existing + 4 new FlushSaves), 0 failed

--- M3 chunk 1 (tasks 1-5) — VERIFIED 116/116 green ---
M3 Task 1: complete (head cbd7e59) [ChecklistItemViewModel]
M3 Task 2: complete (head 9731c34) [fakes + Load] (minor: 2 dead usings)
M3 Task 3: complete (head ad19053) [add/remove + parent updated_at]
M3 Task 4: complete (head 29767cf) [done toggle + strikethrough + done_at]
M3 Task 5: complete (head 7611f48) [FlushSaves auto-tag + manual protection]
M3 Task 6: head 254856f | Failed: 0, Passed: 119 | CommitClient sets IsManual=true, persists, protects from re-tagging
M3 Task 7: head 9165e2e | Failed: 0, Passed: 123 | log_date setter persists to note; Load uses field-direct to skip OnLogDateChanged; CreateChecklistNote static factory places note in 일일업무일지 system group
M3 Task 8: head 00d4554 | Failed: 0, Passed: 126 | MoveItem reorders collection and renumbers sort_order without bumping Note.UpdatedAt; out-of-range index ignored
M3 Task 9: head 0bc6e73 | Failed: 0, Passed: 126 | ChecklistView XAML+code-behind; DateOnlyToDateTimeConverter; debounced TextBox flush; OnClientSelectionChanged→CommitClientCommand; OnUnloaded immediate flush; build green

--- M3 chunk 2 (tasks 6-9) — VERIFIED 126/126 green ---
M3 Task 6: complete (head 254856f) [CommitClient manual correction]
M3 Task 7: complete (head 9165e2e) [log_date + CreateChecklistNote system group]
M3 Task 8: complete (head 00d4554) [MoveItem reorder]
M3 Task 9: complete (head 35bff3b) [ChecklistView XAML + debounce wiring]
*** M3 COMPLETE: 126/126 tests. ***
MINOR (defer): ChecklistView Window.Deactivated flush; debounce 500ms hardcoded (ignores settings); duplicate BoolToVis/BoolToVisibilityConverter keys in App.xaml

M4 Task 1: head 72b4a75 | Failed: 0, Passed: 128 | WeeklyReportViewModel scaffold + default-week selection + fakes; InvariantCulture MM/dd fix
M4 Task 2: head b525040 | Failed: 0, Passed: 132 | BuildOptions from settings, GenerateCommand, ReportText, HasUnclassifiedWarning
M4 Task 3: head 1566ef7 | Failed: 0, Passed: 137 | idempotent reuse, regenerate confirm, system-group placement
M4 Task 4: head 3fde449 | Failed: 0, Passed: 143 | clipboard copy, format toggle reload, WPF weekly report view and DI

--- M4 (tasks 1-4) — VERIFIED 143/143 green ---
M4 Task 1: complete (head 72b4a75) [WeeklyReportViewModel + week pick]
M4 Task 2: complete (head fd75df7) [settings options + Build + warning banner]
M4 Task 3: complete (head 463e9cf) [idempotent reuse / regenerate confirm / system group]
M4 Task 4: complete (head 3fde449) [copy + format toggle + DateOnly conv + WPF view]
*** M4 COMPLETE: 143/143 tests. ***
MINOR (defer): WeeklyReportVM.Generate redundant GetWorkWeek; WeeklyReportView redundant BoolToVis local converter

M5 Task 1: head f79be0e | Failed: 0, Passed: 144 | GroupManagementViewModel Load + canonical test fakes (FakeGroupRepository/FakeNoteRepository/FakeSettingsRepository/FixedTimeProvider) consolidated in Memoria.Tests.Fakes
M5 Task 2: head 992da14 | Failed: 0, Passed: 146 | AddGroup command persists with next SortOrder and DefaultGroupColor; empty list uses SortOrder 0
M5 Task 3: head d51d5fb | Failed: 0, Passed: 149 | RenameGroup with system-group protection; CanModifySelected guards RenameGroupCommand CanExecute
M5 Task 4: head 29711bd | Failed: 0, Passed: 152 | SetGroupColor command; HasSelection CanExecute (selection-only, system groups allowed); NotifyCanExecuteChangedFor wired
M5 Task 5: head bc7cf03 | Failed: 0, Passed: 154 | DeleteGroup with system-group protection (CanModifySelected guards); notes.group_id ON DELETE SET NULL via DB constraint; NotifyCanExecuteChangedFor(DeleteGroupCommand) wired
M5 Task 6: head 21a55e1 | Failed: 0, Passed: 156 | MoveGroup index-based reorder + SortOrder reassignment + persistence via IGroupRepository.Update; out-of-range/no-op guard

--- M5 chunk 1 (tasks 1-6) — VERIFIED 156/156 green ---
M5 Task 1: complete (head 9452aad) [fakes + GroupManagementViewModel load]
M5 Task 2: complete (head edd20b0) [AddGroup]
M5 Task 3: complete (head a91c37d) [RenameGroup + system protect]
M5 Task 4: complete (head 29711bd) [SetGroupColor]
M5 Task 5: complete (head bc7cf03) [DeleteGroup + SET NULL + system protect]
M5 Task 6: complete (head 21a55e1) [MoveGroup reorder]
MINOR (defer): fake test-fidelity (Update by-reference masks persistence calls); FakeNoteRepo _nextId=100/extra Update branch
M5 Task 7: head 07e2058 | Failed: 0, Passed: 158 | MoveNoteToGroup changes GroupId without bumping UpdatedAt; null target sets unclassified
M5 Task 8: head c59e78b | Failed: 0, Passed: 164 | TrashItemViewModel purge-countdown + DisplayTitle fallback; TrashViewModel Load + RetentionDays from settings
M5 Task 9: head b66ba4b | Failed: 0, Passed: 167 | TrashViewModel.DeleteNote SoftDelete + Undo state; DeleteNoteCommand/UndoCommand with CanExecute guard
M5 Task 10: head 3490b90 | Failed: 0, Passed: 169 | TrashViewModel.Restore/Purge commands; restore clears DeletedAt and reloads list; purge permanently removes note and reloads list
M5 Task 11: head 12db6b1 | Failed: 0, Passed: 171 | TrashViewModel.PurgeExpiredOnStartup delegates to INoteRepository.PurgeExpiredTrash(RetentionDays); retention setting read from ISettingsRepository, defaults to 30
M5 Task 12: head 0ca8e78 | Failed: 0, Passed: 171 | DI wiring (AddTransient<GroupManagementViewModel>/AddSingleton<TrashViewModel>) + startup PurgeExpiredOnStartup; TrashView UserControl; MainWindow sidebar context menu + note delete button + Undo toast + drag-drop handlers; all manual checkpoints pending Windows run
