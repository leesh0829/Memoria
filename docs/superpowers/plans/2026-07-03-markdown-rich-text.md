# 마크다운 리치 텍스트 + 이미지 첨부 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 일반(Plain) 메모에 마크다운 서식과 이미지 첨부를 추가하되, 검색·자동저장·크래시복구·기존 메모 호환을 하나도 깨지 않는다.

**Architecture:** `body`는 마크다운 텍스트(TEXT)로 유지하고 `body_format`('plain'|'markdown') 컬럼(스키마 v2)으로 렌더 여부를 구분한다. 편집/미리보기 토글, 미리보기는 Markdig 파싱 → 자체 FlowDocument 렌더러. 이미지는 `%LOCALAPPDATA%\Memoria\attachments\{noteId}\`에 파일로 저장하고 본문은 마크다운 참조.

**Tech Stack:** C#/.NET9, WPF, SQLite(Dapper), Markdig(파서), CommunityToolkit.Mvvm, xUnit + FluentAssertions.

## Global Constraints

- 빌드/테스트는 **Windows `dotnet.exe`를 WSL interop로** 호출. 실행 전 `taskkill.exe /IM Memoria.exe /F 2>/dev/null`.
  - 빌드: `dotnet.exe build "Memoria.sln" -c Release`
  - 테스트: `dotnet.exe test "tests/Memoria.Tests" -c Release`
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 목표: 빌드 **경고 0 / 오류 0**, 기존 **310 테스트 + 신규 테스트 그린**.
- WPF 렌더/클립보드/붙여넣기는 WSL에서 자동 테스트 불가 → **Windows GUI 수동 검증**. 로직은 Core에 두어 테스트 가능하게 유지.
- `body_format` 값 도메인은 문자열 `'plain'` / `'markdown'`. 컬럼 DEFAULT `'plain'`은 기존 행 마이그레이션 전용; 신규 Plain 메모는 생성 코드가 `'markdown'`을 명시 지정.
- 적용 범위는 **Plain 메모만**. Checklist·WeeklyReport는 body 미사용 → 변경 없음.
- SQLite 쓰기는 `lock(_factory.WriteSync)` + `_factory.Write`. 마이그레이션은 트랜잭션 단위.

---

## File Structure

- `src/Memoria.Core/Data/DatabaseInitializer.cs` — 수정: 단발→**스텝형 마이그레이션**, `TargetVersion=2`, V2 스텝(`body_format` 추가).
- `src/Memoria.Core/Models/Models.cs` — 수정: `Note.BodyFormat` 필드.
- `src/Memoria.Core/Data/NoteRepository.cs` — 수정: SelectColumns/INSERT/UPDATE에 `body_format`.
- `src/Memoria.Core/Text/MarkdownText.cs` — 신규: 제목용 마크다운 마커 제거(순수, 테스트 가능).
- `src/Memoria.App/ViewModels/NoteTitleResolver.cs` — 수정: markdown 노트면 마커 제거.
- `src/Memoria.Core/Attachments/IAttachmentService.cs` + `AttachmentService.cs` — 신규: 이미지 저장/해석/삭제.
- `src/Memoria.App/AppPaths.cs` — 수정: `AttachmentsDirectory`.
- `src/Memoria.App/Services/IMarkdownRenderer.cs` + `MarkdownRenderer.cs` — 신규: Markdig AST→FlowDocument.
- `src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs` — 신규: FlowDocumentScrollViewer 첨부 속성.
- `src/Memoria.App/ViewModels/MainViewModel.cs` — 수정: `IsPreviewMode`/`BodyFormat`/`IsMarkdown`/토글·전환 커맨드/`ResolveLiveTitle`.
- `src/Memoria.App/MainWindow.xaml` (+ `.xaml.cs`) — 수정: Plain 에디터 템플릿(툴바/토글/미리보기), 이미지 붙여넣기·삽입.
- `src/Memoria.App/ViewModels/TrashViewModel.cs` — 수정: Purge 시 첨부 삭제.
- `src/Memoria.App/App.xaml.cs` — 수정: DI 등록(IAttachmentService, IMarkdownRenderer).
- `src/Memoria.App/Memoria.App.csproj` — 수정: Markdig 패키지.
- 테스트: `tests/Memoria.Tests/Data/MigrationV2Tests.cs`, `NoteRepositoryTests.cs`(추가), `tests/Memoria.Tests/Core/MarkdownTextTests.cs`, `tests/Memoria.Tests/App/NoteTitleResolverTests.cs`(추가), `tests/Memoria.Tests/Core/AttachmentServiceTests.cs`.

---

## RM1: 스키마 v2 — 스텝형 마이그레이션 + body_format 컬럼 (Core, TDD)

**Files:**
- Modify: `src/Memoria.Core/Data/DatabaseInitializer.cs`
- Test: `tests/Memoria.Tests/Data/MigrationV2Tests.cs`

**Interfaces:**
- Produces: 마이그레이션 후 `notes.body_format TEXT NOT NULL DEFAULT 'plain'` 존재, `PRAGMA user_version = 2`.

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/Data/MigrationV2Tests.cs`

```csharp
using Dapper;
using FluentAssertions;
using Memoria.Core.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Memoria.Tests.Data;

public class MigrationV2Tests
{
    [Fact]
    public void FreshDb_MigratesTo_V2_WithBodyFormatColumn()
    {
        using var db = new TestDb();   // EnsureReady() 실행됨
        using var conn = db.Factory.Open();

        conn.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(2);
        var cols = conn.Query<string>("SELECT name FROM pragma_table_info('notes');");
        cols.Should().Contain("body_format");
    }

    [Fact]
    public void ExistingV1Db_Upgrades_AndDefaultsExistingRowsToPlain()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "memoria_v1_" + System.Guid.NewGuid().ToString("N") + ".db");
        try
        {
            // v1 상태를 흉내: body_format 없는 notes 테이블 + user_version=1 + _migrations(1).
            using (var factory0 = new SqliteConnectionFactory(path))
            {
                var c = factory0.Write;
                c.Execute("CREATE TABLE _migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");
                c.Execute("INSERT INTO _migrations(version, applied_at) VALUES(1, 'x');");
                c.Execute("CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT, body TEXT);");
                c.Execute("INSERT INTO notes(id, title, body) VALUES(1, 't', 'b');");
                c.Execute("PRAGMA user_version = 1;");
            }
            SqliteConnection.ClearAllPools();

            using (var factory1 = new SqliteConnectionFactory(path))
            {
                new DatabaseInitializer(factory1).EnsureReady();   // v1 → v2
                var c = factory1.Write;
                c.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(2);
                c.ExecuteScalar<string>("SELECT body_format FROM notes WHERE id = 1;").Should().Be("plain");
            }
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            foreach (var p in new[] { path, path + "-wal", path + "-shm" })
                if (System.IO.File.Exists(p)) { try { System.IO.File.Delete(p); } catch { } }
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MigrationV2Tests" 2>&1 | tail -8
```
기대: FAIL (user_version가 2가 아님 / body_format 컬럼 없음).

- [ ] **Step 3: 스텝형 마이그레이션 구현** — `DatabaseInitializer.cs`의 `TargetVersion`과 `EnsureReady`를 교체.

`private const long TargetVersion = 1;` 을 `= 2;` 로 변경하고, `EnsureReady` 본문을 아래로 교체:

```csharp
    public void EnsureReady()
    {
        // 마이그레이션은 쓰기이므로 단일 직렬 라이터 락 + 영속 쓰기 연결로 수행(계약 §8).
        lock (_factory.WriteSync)
        {
            var conn = _factory.Write;
            conn.Execute(
                "CREATE TABLE IF NOT EXISTS _migrations (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);");

            var current = conn.ExecuteScalar<long>("PRAGMA user_version;");
            if (current >= TargetVersion) return;

            if (current < 1) ApplyV1(conn);
            if (current < 2) ApplyV2(conn);
        }
    }

    private static void ApplyV1(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute(SchemaV1, transaction: tx);
        SeedV1(conn, tx);
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 1;", transaction: tx);
        tx.Commit();
    }

    private static void ApplyV2(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        conn.Execute("ALTER TABLE notes ADD COLUMN body_format TEXT NOT NULL DEFAULT 'plain';", transaction: tx);
        conn.Execute(
            "INSERT INTO _migrations(version, applied_at) VALUES(2, strftime('%Y-%m-%dT%H:%M:%fZ','now'));",
            transaction: tx);
        conn.Execute("PRAGMA user_version = 2;", transaction: tx);
        tx.Commit();
    }
```

`SchemaV1`(notes에 body_format 없음)과 `SeedV1`는 **그대로 둔다**(v1은 불변 이력). PRAGMA user_version은 `ALTER`로 인해 갱신되지 않으므로 각 스텝에서 명시 설정.

- [ ] **Step 4: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MigrationV2Tests" 2>&1 | tail -8
```
기대: PASS 2건.

- [ ] **Step 5: 커밋**

```bash
git add src/Memoria.Core/Data/DatabaseInitializer.cs tests/Memoria.Tests/Data/MigrationV2Tests.cs
git commit -m "feat(md): schema v2 stepped migration adds notes.body_format

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM2: Note.BodyFormat 모델 + 리포지토리 왕복 (Core, TDD)

**Files:**
- Modify: `src/Memoria.Core/Models/Models.cs`, `src/Memoria.Core/Data/NoteRepository.cs`
- Test: `tests/Memoria.Tests/Data/NoteRepositoryTests.cs` (테스트 추가)

**Interfaces:**
- Produces: `Note.BodyFormat` (string, 기본 `"plain"`); `NoteRepository.Create/Update`가 `body_format` 저장, `Get`이 복원.

- [ ] **Step 1: 실패 테스트 추가** — `NoteRepositoryTests.cs`에 아래 두 테스트 추가

```csharp
    [Fact]
    public void Create_DefaultsBodyFormat_ToPlain()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, Title = "t" });
        sut.Get(id)!.BodyFormat.Should().Be("plain");
    }

    [Fact]
    public void Create_And_Get_RoundTrips_MarkdownFormat()
    {
        using var db = new TestDb();
        var sut = new NoteRepository(db.Factory);
        var id = sut.Create(new Note { Type = NoteType.Plain, Title = "t", BodyFormat = "markdown" });
        sut.Get(id)!.BodyFormat.Should().Be("markdown");
    }
```

- [ ] **Step 2: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~NoteRepositoryTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`BodyFormat` 없음).

- [ ] **Step 3: 모델 필드 추가** — `Models.cs`의 `Note` 클래스, `Body` 아래에 추가

```csharp
    public string BodyFormat { get; set; } = "plain";
```

- [ ] **Step 4: 리포지토리 매핑 추가** — `NoteRepository.cs`

`SelectColumns` 끝에 `body_format` 추가:
```csharp
    private const string SelectColumns =
        "id AS Id, group_id AS GroupId, type AS Type, title AS Title, body AS Body, " +
        "body_format AS BodyFormat, " +
        "log_date AS LogDate, report_format AS ReportFormat, report_week_start AS ReportWeekStart, " +
        "pinned AS Pinned, sort_order AS SortOrder, deleted_at AS DeletedAt, " +
        "created_at AS CreatedAt, updated_at AS UpdatedAt";
```

`Create`의 INSERT: 컬럼 목록에 `body_format`, VALUES에 `@BodyFormat`, 익명 파라미터 객체에 `note.BodyFormat` 추가:
```csharp
            conn.Execute(
                "INSERT INTO notes(group_id, type, title, body, body_format, log_date, report_format, report_week_start, " +
                "pinned, sort_order, deleted_at, created_at, updated_at) " +
                "VALUES(@GroupId, @Type, @Title, @Body, @BodyFormat, @LogDate, @ReportFormat, @ReportWeekStart, " +
                "@Pinned, @SortOrder, @DeletedAt, @CreatedAt, @UpdatedAt);",
                new
                {
                    note.GroupId,
                    Type = NoteTypeToString(note.Type),
                    note.Title,
                    note.Body,
                    note.BodyFormat,
                    note.LogDate,
                    ReportFormat = ReportFormatToString(note.ReportFormat),
                    note.ReportWeekStart,
                    note.Pinned,
                    note.SortOrder,
                    note.DeletedAt,
                    note.CreatedAt,
                    note.UpdatedAt,
                });
```

`Update`의 UPDATE SET에 `body_format = @BodyFormat` 추가, 파라미터 객체에 `note.BodyFormat` 추가:
```csharp
            _factory.Write.Execute(
                "UPDATE notes SET group_id = @GroupId, type = @Type, title = @Title, body = @Body, " +
                "body_format = @BodyFormat, " +
                "log_date = @LogDate, report_format = @ReportFormat, report_week_start = @ReportWeekStart, " +
                "pinned = @Pinned, sort_order = @SortOrder, deleted_at = @DeletedAt, " +
                "created_at = @CreatedAt, updated_at = @UpdatedAt WHERE id = @Id;",
                new
                {
                    note.GroupId,
                    Type = NoteTypeToString(note.Type),
                    note.Title,
                    note.Body,
                    note.BodyFormat,
                    note.LogDate,
                    ReportFormat = ReportFormatToString(note.ReportFormat),
                    note.ReportWeekStart,
                    note.Pinned,
                    note.SortOrder,
                    note.DeletedAt,
                    note.CreatedAt,
                    note.UpdatedAt,
                    note.Id,
                });
```

- [ ] **Step 5: 통과 확인 (신규 + 기존 리포지토리 테스트)**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~NoteRepositoryTests" 2>&1 | tail -8
```
기대: PASS(신규 2 + 기존 6).

- [ ] **Step 6: 커밋**

```bash
git add src/Memoria.Core/Models/Models.cs src/Memoria.Core/Data/NoteRepository.cs tests/Memoria.Tests/Data/NoteRepositoryTests.cs
git commit -m "feat(md): persist Note.BodyFormat via NoteRepository

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM3: 제목용 마크다운 마커 제거 (Core 헬퍼 + NoteTitleResolver, TDD)

**Files:**
- Create: `src/Memoria.Core/Text/MarkdownText.cs`
- Modify: `src/Memoria.App/ViewModels/NoteTitleResolver.cs`, `src/Memoria.App/ViewModels/MainViewModel.cs`(ResolveLiveTitle)
- Test: `tests/Memoria.Tests/Core/MarkdownTextTests.cs`, `tests/Memoria.Tests/App/NoteTitleResolverTests.cs`(추가)

**Interfaces:**
- Produces: `Memoria.Core.Text.MarkdownText.StripMarkers(string line) -> string`.

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/Core/MarkdownTextTests.cs`

```csharp
using FluentAssertions;
using Memoria.Core.Text;
using Xunit;

namespace Memoria.Tests.Core;

public class MarkdownTextTests
{
    [Theory]
    [InlineData("# 제목", "제목")]
    [InlineData("###   여백 제목  ", "여백 제목")]
    [InlineData("- 항목", "항목")]
    [InlineData("* 항목", "항목")]
    [InlineData("1. 첫째", "첫째")]
    [InlineData("> 인용", "인용")]
    [InlineData("**굵게** 텍스트", "굵게 텍스트")]
    [InlineData("`code`", "code")]
    [InlineData("일반 텍스트", "일반 텍스트")]
    public void StripMarkers_RemovesLeadingBlockAndInlineMarks(string input, string expected)
        => MarkdownText.StripMarkers(input).Should().Be(expected);
}
```

- [ ] **Step 2: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MarkdownTextTests" 2>&1 | tail -8
```
기대: 컴파일 실패(`MarkdownText` 없음).

- [ ] **Step 3: 헬퍼 구현** — `src/Memoria.Core/Text/MarkdownText.cs`

```csharp
using System.Text.RegularExpressions;

namespace Memoria.Core.Text;

/// <summary>제목 표시용으로 마크다운 마커를 제거한다(렌더링용 아님).</summary>
public static class MarkdownText
{
    public static string StripMarkers(string line)
    {
        var s = line.Trim();
        s = Regex.Replace(s, @"^#{1,6}\s+", "");        // 제목
        s = Regex.Replace(s, @"^>\s+", "");              // 인용
        s = Regex.Replace(s, @"^([-*+]|\d+\.)\s+", "");  // 목록(글머리/번호)
        s = s.Replace("**", "").Replace("__", "");       // 굵게
        s = s.Replace("*", "").Replace("`", "");         // 기울임/코드
        return s.Trim();
    }
}
```

- [ ] **Step 4: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~MarkdownTextTests" 2>&1 | tail -8
```
기대: PASS.

- [ ] **Step 5: NoteTitleResolver 실패 테스트 추가** — `NoteTitleResolverTests.cs`

```csharp
    [Fact]
    public void Markdown_note_strips_leading_markers_from_body_title()
    {
        var note = new Note { Title = null, Body = "# 제목\n본문", BodyFormat = "markdown" };
        NoteTitleResolver.Resolve(note).Should().Be("제목");
    }

    [Fact]
    public void Plain_note_keeps_markdown_like_body_line_verbatim()
    {
        var note = new Note { Title = null, Body = "# 제목", BodyFormat = "plain" };
        NoteTitleResolver.Resolve(note).Should().Be("# 제목");
    }
```

- [ ] **Step 6: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~NoteTitleResolverTests" 2>&1 | tail -8
```
기대: FAIL(markdown 케이스가 "# 제목" 반환).

- [ ] **Step 7: NoteTitleResolver 수정** — 첫 비어있지 않은 줄에 markdown이면 StripMarkers 적용

```csharp
using Memoria.Core.Models;
using Memoria.Core.Text;

namespace Memoria.App.ViewModels;

/// <summary>
/// 제목 표시 규칙(§5.1): title이 비면 body 첫 비어있지 않은 줄을 표시용 제목으로.
/// markdown 노트는 선행 마커를 제거해 표시.
/// </summary>
public static class NoteTitleResolver
{
    public static string Resolve(Note note)
    {
        if (!string.IsNullOrWhiteSpace(note.Title))
            return note.Title!.Trim();

        if (!string.IsNullOrEmpty(note.Body))
        {
            var isMarkdown = note.BodyFormat == "markdown";
            foreach (var line in note.Body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return isMarkdown ? MarkdownText.StripMarkers(trimmed) : trimmed;
            }
        }
        return "(제목 없음)";
    }
}
```

- [ ] **Step 8: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~NoteTitleResolverTests" 2>&1 | tail -8
```
기대: PASS(신규 2 + 기존 3).

- [ ] **Step 9: 라이브 제목도 일치시키기** — `MainViewModel.ResolveLiveTitle`(라인 ~403). 현재 노트가 markdown이면 동일 규칙 적용. `_current`가 있으므로 그 BodyFormat 사용.

```csharp
    private string ResolveLiveTitle()
    {
        if (!string.IsNullOrWhiteSpace(EditorTitle)) return EditorTitle.Trim();
        if (!string.IsNullOrEmpty(EditorBody))
        {
            var isMarkdown = _current?.BodyFormat == "markdown";
            foreach (var line in EditorBody.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                return isMarkdown ? Memoria.Core.Text.MarkdownText.StripMarkers(trimmed) : trimmed;
            }
        }
        return "(제목 없음)";
    }
```

- [ ] **Step 10: 빌드 확인 후 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
git add src/Memoria.Core/Text/MarkdownText.cs src/Memoria.App/ViewModels/NoteTitleResolver.cs src/Memoria.App/ViewModels/MainViewModel.cs tests/Memoria.Tests/Core/MarkdownTextTests.cs tests/Memoria.Tests/App/NoteTitleResolverTests.cs
git commit -m "feat(md): markdown-aware title extraction (strip leading markers)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM4: 첨부 서비스 + 경로 + DI + purge 훅 (Core TDD + App 배선)

**Files:**
- Create: `src/Memoria.Core/Attachments/IAttachmentService.cs`, `src/Memoria.Core/Attachments/AttachmentService.cs`
- Modify: `src/Memoria.App/AppPaths.cs`, `src/Memoria.App/App.xaml.cs`, `src/Memoria.App/ViewModels/TrashViewModel.cs`
- Test: `tests/Memoria.Tests/Core/AttachmentServiceTests.cs`

**Interfaces:**
- Produces:
  - `string IAttachmentService.SaveImage(int noteId, byte[] bytes, string ext) -> "attachments/{noteId}/{guid}.{ext}"`
  - `string SaveFile(int noteId, string sourcePath) -> 상대경로`
  - `string ResolveToAbsolute(string relativePath) -> 절대경로`
  - `void DeleteForNote(int noteId)`

- [ ] **Step 1: 실패 테스트 작성** — `tests/Memoria.Tests/Core/AttachmentServiceTests.cs`

```csharp
using System.IO;
using FluentAssertions;
using Memoria.Core.Attachments;
using Xunit;

namespace Memoria.Tests.Core;

public class AttachmentServiceTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "memoria_att_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void SaveImage_WritesFile_AndReturnsRelativePath_ResolvableToAbsolute()
    {
        var dir = NewTempDir();
        try
        {
            var sut = new AttachmentService(dir);
            var rel = sut.SaveImage(7, new byte[] { 1, 2, 3 }, "png");

            rel.Should().StartWith("attachments/7/").And.EndWith(".png");
            var abs = sut.ResolveToAbsolute(rel);
            File.Exists(abs).Should().BeTrue();
            File.ReadAllBytes(abs).Should().Equal(1, 2, 3);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SaveFile_CopiesSource_PreservingExtension()
    {
        var dir = NewTempDir();
        try
        {
            var src = Path.Combine(dir, "src.jpg");
            File.WriteAllBytes(src, new byte[] { 9 });
            var sut = new AttachmentService(dir);

            var rel = sut.SaveFile(3, src);

            rel.Should().StartWith("attachments/3/").And.EndWith(".jpg");
            File.Exists(sut.ResolveToAbsolute(rel)).Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DeleteForNote_RemovesNoteFolder()
    {
        var dir = NewTempDir();
        try
        {
            var sut = new AttachmentService(dir);
            var rel = sut.SaveImage(5, new byte[] { 1 }, "png");
            sut.DeleteForNote(5);
            File.Exists(sut.ResolveToAbsolute(rel)).Should().BeFalse();
            Directory.Exists(Path.Combine(dir, "attachments", "5")).Should().BeFalse();
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: 실패 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~AttachmentServiceTests" 2>&1 | tail -8
```
기대: 컴파일 실패.

- [ ] **Step 3: 인터페이스 + 구현** — `IAttachmentService.cs`

```csharp
namespace Memoria.Core.Attachments;

public interface IAttachmentService
{
    string SaveImage(int noteId, byte[] bytes, string ext);
    string SaveFile(int noteId, string sourcePath);
    string ResolveToAbsolute(string relativePath);
    void DeleteForNote(int noteId);
}
```

`AttachmentService.cs`

```csharp
using System;
using System.IO;

namespace Memoria.Core.Attachments;

/// <summary>이미지 파일 저장/경로 해석/노트별 삭제. base = DataDirectory, 루트 = base/attachments.</summary>
public sealed class AttachmentService : IAttachmentService
{
    private readonly string _dataDir;
    public AttachmentService(string dataDirectory) => _dataDir = dataDirectory;

    private string Root => Path.Combine(_dataDir, "attachments");

    public string SaveImage(int noteId, byte[] bytes, string ext)
    {
        var name = Guid.NewGuid().ToString("N") + "." + ext.TrimStart('.');
        var dir = Path.Combine(Root, noteId.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        return $"attachments/{noteId}/{name}";
    }

    public string SaveFile(int noteId, string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).TrimStart('.');
        var name = Guid.NewGuid().ToString("N") + "." + ext;
        var dir = Path.Combine(Root, noteId.ToString());
        Directory.CreateDirectory(dir);
        File.Copy(sourcePath, Path.Combine(dir, name));
        return $"attachments/{noteId}/{name}";
    }

    public string ResolveToAbsolute(string relativePath) =>
        Path.GetFullPath(Path.Combine(_dataDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void DeleteForNote(int noteId)
    {
        var dir = Path.Combine(Root, noteId.ToString());
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 4: 통과 확인**

```bash
dotnet.exe test "tests/Memoria.Tests" -c Release --filter "FullyQualifiedName~AttachmentServiceTests" 2>&1 | tail -8
```
기대: PASS 3건.

- [ ] **Step 5: AppPaths에 첨부 경로 추가** — `AppPaths.cs`

`RecoveryDirectory` 아래에 추가하고 `EnsureDirectories`에서 생성:
```csharp
    public static string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
    }
```

- [ ] **Step 6: DI 등록** — `App.xaml.cs`의 서비스 등록부(라인 ~73, RecoveryJournal 옆)에 추가

```csharp
        sc.AddSingleton<Memoria.Core.Attachments.IAttachmentService>(
            _ => new Memoria.Core.Attachments.AttachmentService(AppPaths.DataDirectory));
```

- [ ] **Step 7: purge 시 첨부 삭제** — `TrashViewModel.cs`

생성자에 `IAttachmentService` 주입(기존 주입 스타일 따라 필드 추가), `Purge`에서 폴더 삭제 호출:
```csharp
    // 필드/생성자: 기존 _notes 옆에 추가
    private readonly Memoria.Core.Attachments.IAttachmentService _attachments;
    // 생성자 파라미터에 IAttachmentService attachments 추가 후: _attachments = attachments;

    public void Purge(int noteId)
    {
        _notes.Purge(noteId);            // checklist_items CASCADE
        _attachments.DeleteForNote(noteId);
        // (기존 목록 갱신 로직 유지)
    }
```
> DI 컨테이너가 `TrashViewModel`을 생성하므로 새 파라미터는 자동 주입된다. `PurgeExpiredTrash`(자동 만료)로 지워지는 노트의 첨부는 이번 범위에서 정리하지 않는다(스펙 §4.5: 고아 정리는 후속). 코드 옆에 주석으로 남길 것.

- [ ] **Step 8: 전체 빌드/테스트 + 커밋**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -5
dotnet.exe test "tests/Memoria.Tests" -c Release 2>&1 | tail -4
git add src/Memoria.Core/Attachments/ src/Memoria.App/AppPaths.cs src/Memoria.App/App.xaml.cs src/Memoria.App/ViewModels/TrashViewModel.cs tests/Memoria.Tests/Core/AttachmentServiceTests.cs
git commit -m "feat(md): attachment service (save/resolve/delete) + purge hook + paths + DI

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM5: Markdig + FlowDocument 렌더러 (App, WPF — GUI 검증)

**Files:**
- Modify: `src/Memoria.App/Memoria.App.csproj`
- Create: `src/Memoria.App/Services/IMarkdownRenderer.cs`, `src/Memoria.App/Services/MarkdownRenderer.cs`
- Modify: `src/Memoria.App/App.xaml.cs` (DI)

**Interfaces:**
- Consumes: `IAttachmentService.ResolveToAbsolute` (RM4).
- Produces: `System.Windows.Documents.FlowDocument IMarkdownRenderer.Render(string markdown)`.

> WPF 타입(FlowDocument)이라 WSL 자동 테스트 불가. 빌드 성공 + GUI 검증(RM8)으로 확인.

- [ ] **Step 1: Markdig 패키지 추가** — `Memoria.App.csproj`의 PackageReference ItemGroup(라인 7~11)에 추가

```xml
    <PackageReference Include="Markdig" Version="0.38.0" />
```
> 0.38.0이 복원 안 되면 최신 안정 버전으로. 복원 확인: `dotnet.exe restore "Memoria.sln" 2>&1 | tail -3`.

- [ ] **Step 2: 렌더러 인터페이스** — `src/Memoria.App/Services/IMarkdownRenderer.cs`

```csharp
using System.Windows.Documents;

namespace Memoria.App.Services;

public interface IMarkdownRenderer
{
    FlowDocument Render(string? markdown);
}
```

- [ ] **Step 3: 렌더러 구현** — `src/Memoria.App/Services/MarkdownRenderer.cs`

Markdig AST를 순회해 FlowDocument를 만든다. 실패 시 원문 텍스트로 폴백(크래시 금지). 테마는 `SetResourceReference`로 연동.

```csharp
using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Memoria.Core.Attachments;
using WBlock = System.Windows.Documents.Block;
using WInline = System.Windows.Documents.Inline;

namespace Memoria.App.Services;

/// <summary>Markdig AST → WPF FlowDocument. 테마 브러시 연동, 실패 시 원문 폴백.</summary>
public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private readonly IAttachmentService _attachments;
    private readonly MarkdownPipeline _pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public MarkdownRenderer(IAttachmentService attachments) => _attachments = attachments;

    public FlowDocument Render(string? markdown)
    {
        var flow = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14 };
        flow.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Foreground");
        try
        {
            var doc = Markdown.Parse(markdown ?? "", _pipeline);
            foreach (var block in doc)
                if (ConvertBlock(block) is { } b) flow.Blocks.Add(b);
        }
        catch
        {
            flow.Blocks.Clear();
            flow.Blocks.Add(new Paragraph(new Run(markdown ?? "")));
        }
        return flow;
    }

    private WBlock? ConvertBlock(Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var p = new Paragraph { FontWeight = FontWeights.Bold };
                p.FontSize = h.Level switch { 1 => 22, 2 => 19, 3 => 17, _ => 15 };
                p.Margin = new Thickness(0, 8, 0, 4);
                AddInlines(p.Inlines, h.Inline);
                return p;
            }
            case ParagraphBlock para:
            {
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
                AddInlines(p.Inlines, para.Inline);
                return p;
            }
            case Markdig.Syntax.QuoteBlock quote:
            {
                var section = new Section { Margin = new Thickness(8, 2, 0, 6) };
                section.SetResourceReference(Section.ForegroundProperty, "Brush.SecondaryForeground");
                section.BorderThickness = new Thickness(3, 0, 0, 0);
                section.SetResourceReference(Section.BorderBrushProperty, "Brush.Border");
                section.Padding = new Thickness(8, 0, 0, 0);
                foreach (var child in quote)
                    if (ConvertBlock(child) is { } cb) section.Blocks.Add(cb);
                return section;
            }
            case ListBlock list:
            {
                var wlist = new List
                {
                    MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 2, 0, 6),
                    Padding = new Thickness(20, 0, 0, 0),
                };
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var li = new ListItem();
                    foreach (var child in item)
                        if (ConvertBlock(child) is { } cb) li.Blocks.Add(cb);
                    if (li.Blocks.Count == 0) li.Blocks.Add(new Paragraph());
                    wlist.ListItems.Add(li);
                }
                return wlist;
            }
            case Markdig.Syntax.CodeBlock code:
            {
                var text = string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString()));
                var p = new Paragraph(new Run(text))
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Margin = new Thickness(0, 2, 0, 6),
                    Padding = new Thickness(8),
                };
                p.SetResourceReference(Paragraph.BackgroundProperty, "Brush.ListItemHover");
                return p;
            }
            case ThematicBreakBlock:
            {
                var p = new Paragraph { Margin = new Thickness(0, 6, 0, 6) };
                p.Inlines.Add(new Run(new string('─', 20)));
                p.SetResourceReference(Paragraph.ForegroundProperty, "Brush.Border");
                return p;
            }
            default:
                return null;
        }
    }

    private void AddInlines(InlineCollection target, ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
            if (ConvertInline(inline) is { } wi) target.Add(wi);
    }

    private WInline? ConvertInline(Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                return new Run(lit.Content.ToString());
            case LineBreakInline:
                return new LineBreak();
            case CodeInline ci:
            {
                var run = new Run(ci.Content) { FontFamily = new FontFamily("Consolas, Courier New, monospace") };
                return run;
            }
            case EmphasisInline em:
            {
                Span span = em.DelimiterCount >= 2 ? new Bold() : new Italic();
                foreach (var child in em)
                    if (ConvertInline(child) is { } wi) span.Inlines.Add(wi);
                return span;
            }
            case LinkInline link when link.IsImage:
                return BuildImage(link);
            case LinkInline link:
            {
                var h = new Hyperlink { NavigateUri = SafeUri(link.Url) };
                h.SetResourceReference(Hyperlink.ForegroundProperty, "Brush.Accent");
                foreach (var child in link)
                    if (ConvertInline(child) is { } wi) h.Inlines.Add(wi);
                if (h.Inlines.Count == 0) h.Inlines.Add(new Run(link.Url ?? ""));
                return h;
            }
            default:
                // 기타 컨테이너 인라인은 자식만 펼친다.
                if (inline is ContainerInline c)
                {
                    var span = new Span();
                    foreach (var child in c)
                        if (ConvertInline(child) is { } wi) span.Inlines.Add(wi);
                    return span;
                }
                return null;
        }
    }

    private WInline BuildImage(LinkInline link)
    {
        try
        {
            var abs = _attachments.ResolveToAbsolute(link.Url ?? "");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(abs);
            bmp.EndInit();
            var img = new System.Windows.Controls.Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 600,
                MaxHeight = 400,
            };
            return new InlineUIContainer(img);
        }
        catch
        {
            return new Run($"[이미지: {link.Url}]");
        }
    }

    private static Uri? SafeUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var u) ? u : null;
    }
}
```
> `WBlock`/`WInline`은 파일 상단 `using WBlock = System.Windows.Documents.Block;` / `using WInline = System.Windows.Documents.Inline;` 별칭. `ConvertBlock`은 `Markdig.Syntax.Block` 하나만 받는다.

- [ ] **Step 4: DI 등록** — `App.xaml.cs`, IAttachmentService 등록 아래에 추가

```csharp
        sc.AddSingleton<Memoria.App.Services.IMarkdownRenderer>(
            sp => new Memoria.App.Services.MarkdownRenderer(
                sp.GetRequiredService<Memoria.Core.Attachments.IAttachmentService>()));
```

- [ ] **Step 5: 빌드 확인** (경고 0/오류 0)

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
```
기대: 경고 0, 오류 0. (더미 오버로드를 지웠는지 확인. 미사용 using 정리.)

- [ ] **Step 6: 커밋**

```bash
git add src/Memoria.App/Memoria.App.csproj src/Memoria.App/Services/IMarkdownRenderer.cs src/Memoria.App/Services/MarkdownRenderer.cs src/Memoria.App/App.xaml.cs
git commit -m "feat(md): Markdig FlowDocument renderer (theme-aware, image + fallback)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM6: 에디터 UI — 토글/미리보기/툴바/신규 markdown 기본/전환 (App, WPF — GUI 검증)

**Files:**
- Modify: `src/Memoria.App/ViewModels/MainViewModel.cs`, `src/Memoria.App/MainWindow.xaml`, `src/Memoria.App/MainWindow.xaml.cs`
- Create: `src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs`

**Interfaces:**
- Consumes: `IMarkdownRenderer` (RM5), `Note.BodyFormat` (RM2).
- Produces: VM 속성 `IsPreviewMode`(bool, TwoWay), `BodyFormat`(string), `IsMarkdown`/`ShowSource`/`ShowPreview`/`ShowToolbar`(계산), 커맨드 `TogglePreviewCommand`, `ConvertToMarkdownCommand`.

- [ ] **Step 1: VM 상태/커맨드 추가** — `MainViewModel.cs`

기존 `editorTitle`/`editorBody` ObservableProperty 근처(라인 ~50)에 추가:
```csharp
    [ObservableProperty] private bool isPreviewMode = true;
    [ObservableProperty] private string bodyFormat = "plain";

    public bool IsMarkdown => BodyFormat == "markdown";
    public bool ShowToolbar => IsMarkdown && !IsPreviewMode;
    public bool ShowPreview => IsMarkdown && IsPreviewMode;
    public bool ShowSource  => !ShowPreview;   // plain이거나 편집 모드

    partial void OnIsPreviewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowSource));
    }

    partial void OnBodyFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsMarkdown));
        OnPropertyChanged(nameof(ShowToolbar));
        OnPropertyChanged(nameof(ShowPreview));
        OnPropertyChanged(nameof(ShowSource));
    }

    [RelayCommand]
    private void TogglePreview() => IsPreviewMode = !IsPreviewMode;

    [RelayCommand]
    private void ConvertToMarkdown()
    {
        if (_current is null || _current.BodyFormat == "markdown") return;
        _current.BodyFormat = "markdown";
        _noteRepo.Update(_current);         // 즉시 저장(디바운스 밖)
        BodyFormat = "markdown";
        IsPreviewMode = false;              // 전환 직후 편집 모드
        UpdateListItemTitle(_current.Id, ResolveLiveTitle());
    }
```

- [ ] **Step 2: OpenNote에서 포맷/모드 초기화** — `MainViewModel.OpenNote`(라인 ~363), `EditorBody = ...` 다음에 추가

```csharp
        BodyFormat = note.BodyFormat;
        // 새/빈 본문 → 편집 모드, 내용 있으면 미리보기(markdown 노트만 미리보기 의미 있음).
        IsPreviewMode = note.BodyFormat == "markdown" && !string.IsNullOrEmpty(note.Body);
```

- [ ] **Step 3: 신규 일반 메모는 markdown 기본** — `MainViewModel.NewPlainNote`(라인 ~228), Note 이니셜라이저에 추가

```csharp
        var note = new Note
        {
            Type = NoteType.Plain,
            GroupId = SelectedNode is { Kind: SidebarNodeKind.Group } ? SelectedNode.GroupId : null,
            Title = null,
            Body = "",
            BodyFormat = "markdown",
            CreatedAt = now,
            UpdatedAt = now,
        };
```

- [ ] **Step 4: 미리보기 첨부 동작** — `src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs`

FlowDocumentScrollViewer에 마크다운 문자열/활성 상태를 바인딩하면 렌더러로 Document를 채운다.

```csharp
using System.Windows;
using System.Windows.Controls;
using Memoria.App.Services;

namespace Memoria.App.Behaviors;

/// <summary>FlowDocumentScrollViewer.Markdown/Active 첨부 속성 → 렌더러로 Document 갱신.</summary>
public static class MarkdownPreviewBehavior
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.RegisterAttached("Markdown", typeof(string), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty ActiveProperty =
        DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(MarkdownPreviewBehavior),
            new PropertyMetadata(false, OnChanged));

    public static void SetMarkdown(DependencyObject o, string v) => o.SetValue(MarkdownProperty, v);
    public static string GetMarkdown(DependencyObject o) => (string)o.GetValue(MarkdownProperty);
    public static void SetActive(DependencyObject o, bool v) => o.SetValue(ActiveProperty, v);
    public static bool GetActive(DependencyObject o) => (bool)o.GetValue(ActiveProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowDocumentScrollViewer viewer) return;
        if (!GetActive(viewer)) { viewer.Document = null; return; }
        var renderer = AppServices.Resolve<IMarkdownRenderer>();
        viewer.Document = renderer.Render(GetMarkdown(viewer));
    }
}
```
> `AppServices.Resolve<T>()`는 이 코드베이스의 서비스 로케이터(App.xaml.cs 라인 ~112, 135, 160 참조).

- [ ] **Step 5: XAML — Plain 에디터 템플릿 교체** — `MainWindow.xaml` 라인 199~233의 `DataTemplate DataType="{x:Type vm:MainViewModel}"` 안을 아래로 교체. (제목·헤더 Border는 유지, 본문 영역만 확장.)

먼저 상단 `<Window ...>`에 네임스페이스/컨버터가 없으면 확인: `BooleanToVisibilityConverter`는 표준 제공. `xmlns:beh="clr-namespace:Memoria.App.Behaviors"` 추가.

템플릿 교체:
```xml
                    <DataTemplate DataType="{x:Type vm:MainViewModel}">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />  <!-- 제목 -->
                                <RowDefinition Height="Auto" />  <!-- 생성/수정일 + 저장상태 + 모드 버튼 -->
                                <RowDefinition Height="Auto" />  <!-- 툴바(편집 모드) -->
                                <RowDefinition Height="*" />     <!-- 본문(소스/미리보기) -->
                            </Grid.RowDefinitions>

                            <Border Grid.Row="0" BorderBrush="{DynamicResource Brush.Border}" BorderThickness="0,0,0,1">
                                <TextBox Text="{Binding EditorTitle, UpdateSourceTrigger=PropertyChanged}"
                                         FontSize="20" Padding="0,2,0,6" BorderThickness="0"
                                         Background="Transparent"
                                         Foreground="{DynamicResource Brush.Foreground}" />
                            </Border>

                            <Border Grid.Row="1" BorderBrush="{DynamicResource Brush.Border}" BorderThickness="0,0,0,1"
                                    Padding="0,4,0,6">
                                <Grid>
                                    <TextBlock Text="{Binding HeaderText}" HorizontalAlignment="Left"
                                               Foreground="{DynamicResource Brush.SecondaryForeground}" />
                                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                        <TextBlock Text="{Binding SaveStatus}" VerticalAlignment="Center"
                                                   Margin="0,0,8,0"
                                                   Foreground="{DynamicResource Brush.SecondaryForeground}" />
                                        <!-- markdown 노트: 미리보기/편집 토글 -->
                                        <Button Content="{Binding IsPreviewMode, Converter={StaticResource PreviewToggleLabel}}"
                                                Command="{Binding TogglePreviewCommand}" Padding="8,2"
                                                Visibility="{Binding IsMarkdown, Converter={StaticResource BoolToVis}}" />
                                        <!-- plain 노트: 마크다운으로 전환 -->
                                        <Button Content="마크다운으로 전환" Command="{Binding ConvertToMarkdownCommand}"
                                                Padding="8,2"
                                                Visibility="{Binding IsMarkdown, Converter={StaticResource InverseBoolToVis}}" />
                                    </StackPanel>
                                </Grid>
                            </Border>

                            <!-- 툴바: 편집 모드(markdown)일 때만 -->
                            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,6,0,0"
                                        Visibility="{Binding ShowToolbar, Converter={StaticResource BoolToVis}}">
                                <Button Content="B" FontWeight="Bold" Width="28" Tag="bold" Click="OnMdToolbarClick"/>
                                <Button Content="I" FontStyle="Italic" Width="28" Tag="italic" Click="OnMdToolbarClick"/>
                                <Button Content="H" Width="28" Tag="heading" Click="OnMdToolbarClick"/>
                                <Button Content="•" Width="28" Tag="ul" Click="OnMdToolbarClick"/>
                                <Button Content="1." Width="28" Tag="ol" Click="OnMdToolbarClick"/>
                                <Button Content="🔗" Width="28" Tag="link" Click="OnMdToolbarClick"/>
                                <Button Content="🖼" Width="28" Tag="image" Click="OnInsertImageClick"/>
                            </StackPanel>

                            <!-- 본문: 소스 편집 -->
                            <TextBox Grid.Row="3" x:Name="BodyEditor"
                                     Text="{Binding EditorBody, UpdateSourceTrigger=PropertyChanged}"
                                     AcceptsReturn="True" AcceptsTab="True" TextWrapping="Wrap"
                                     VerticalScrollBarVisibility="Auto" Margin="0,8,0,0" Padding="0"
                                     BorderThickness="0" Background="Transparent"
                                     Foreground="{DynamicResource Brush.Foreground}"
                                     PreviewKeyDown="BodyEditor_PreviewKeyDown"
                                     Visibility="{Binding ShowSource, Converter={StaticResource BoolToVis}}" />

                            <!-- 본문: 렌더 미리보기 -->
                            <FlowDocumentScrollViewer Grid.Row="3" Margin="0,8,0,0"
                                     VerticalScrollBarVisibility="Auto"
                                     Background="Transparent"
                                     beh:MarkdownPreviewBehavior.Active="{Binding ShowPreview}"
                                     beh:MarkdownPreviewBehavior.Markdown="{Binding EditorBody}"
                                     Visibility="{Binding ShowPreview, Converter={StaticResource BoolToVis}}" />
                        </Grid>
                    </DataTemplate>
```

- [ ] **Step 6: 컨버터/리소스 등록** — `MainWindow.xaml`의 `<Window.Resources>`(또는 상위 리소스)에 없으면 추가

```xml
        <BooleanToVisibilityConverter x:Key="BoolToVis" />
        <conv:InverseBooleanToVisibilityConverter x:Key="InverseBoolToVis" />
        <conv:PreviewToggleLabelConverter x:Key="PreviewToggleLabel" />
```
> `conv` 네임스페이스가 없으면 컨버터 폴더 네임스페이스로 `xmlns:conv=...` 추가. 두 컨버터가 없으면 생성:

`src/Memoria.App/Converters/InverseBooleanToVisibilityConverter.cs`
```csharp
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Memoria.App.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Collapsed;
}
```
`src/Memoria.App/Converters/PreviewToggleLabelConverter.cs`
```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace Memoria.App.Converters;

// IsPreviewMode == true → "편집"(누르면 편집으로), false → "미리보기".
public sealed class PreviewToggleLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? "✎ 편집" : "👁 미리보기";
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
```
> 기존 코드베이스에 이미 컨버터 폴더/네임스페이스가 있으면 그 규칙을 따를 것(예: `Memoria.App.Converters`). `CountToVisibility` 컨버터가 이미 있으므로(라인 143) 같은 네임스페이스에 둔다.

- [ ] **Step 7: 코드비하인드 — 툴바 문법 삽입** — `MainWindow.xaml.cs`

```csharp
    private void OnMdToolbarClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag }) return;
        if (FindBodyEditor() is not System.Windows.Controls.TextBox tb) return;
        WrapOrInsert(tb, tag);
    }

    // 현재 Plain 에디터의 본문 TextBox를 찾는다(DataTemplate 내부라 이름 직접참조 불가할 수 있음).
    private System.Windows.Controls.TextBox? FindBodyEditor()
        => FindDescendant<System.Windows.Controls.TextBox>(this, "BodyEditor");

    private static void WrapOrInsert(System.Windows.Controls.TextBox tb, string tag)
    {
        int start = tb.SelectionStart, len = tb.SelectionLength;
        string sel = tb.SelectedText ?? "";
        string repl; int caret;
        switch (tag)
        {
            case "bold":   repl = $"**{sel}**"; caret = start + 2 + sel.Length; break;
            case "italic": repl = $"*{sel}*";   caret = start + 1 + sel.Length; break;
            case "heading":repl = $"# {sel}";   caret = start + 2 + sel.Length; break;
            case "ul":     repl = $"- {sel}";   caret = start + 2 + sel.Length; break;
            case "ol":     repl = $"1. {sel}";  caret = start + 3 + sel.Length; break;
            case "link":   repl = $"[{sel}](url)"; caret = start + repl.Length; break;
            default: return;
        }
        tb.Text = tb.Text.Remove(start, len).Insert(start, repl);
        tb.CaretIndex = caret;
        tb.Focus();
    }
```
> `FindDescendant<T>(root, name)` 헬퍼가 없으면 VisualTreeHelper로 이름 일치 자손을 찾는 정적 메서드를 추가한다:
```csharp
    private static T? FindDescendant<T>(System.Windows.DependencyObject root, string name)
        where T : System.Windows.FrameworkElement
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T fe && fe.Name == name) return fe;
            if (FindDescendant<T>(child, name) is { } found) return found;
        }
        return null;
    }
```

- [ ] **Step 8: 빌드 확인**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
```
기대: 경고 0, 오류 0. (`OnInsertImageClick`/`BodyEditor_PreviewKeyDown`는 RM7에서 추가 — 우선 빈 핸들러 stub를 두어 빌드 통과시킨 뒤 RM7에서 구현. 또는 RM7과 함께 커밋.)

> 빌드를 통과시키기 위한 임시 stub(코드비하인드):
```csharp
    private void OnInsertImageClick(object sender, System.Windows.RoutedEventArgs e) { /* RM7 */ }
    private void BodyEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { /* RM7 */ }
```

- [ ] **Step 9: 커밋**

```bash
git add src/Memoria.App/ViewModels/MainViewModel.cs src/Memoria.App/MainWindow.xaml src/Memoria.App/MainWindow.xaml.cs src/Memoria.App/Behaviors/MarkdownPreviewBehavior.cs src/Memoria.App/Converters/
git commit -m "feat(md): editor toggle + preview + toolbar + markdown-default new notes

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM7: 이미지 붙여넣기 + 파일 삽입 (App, WPF — GUI 검증)

**Files:**
- Modify: `src/Memoria.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `IAttachmentService.SaveImage/SaveFile` (RM4), 현재 노트 id(`ViewModel.CurrentEditorNoteId` 또는 `_current`).

> 현재 노트 id 접근: MainViewModel에 이미 내부 `_current`가 있다. 코드비하인드에서 쓰도록 `public int? CurrentNoteId => _current?.Id;` 를 MainViewModel에 노출(라인 ~50 근처 프로퍼티로 추가). 없으면 이 스텝에서 추가.

- [ ] **Step 1: 현재 노트 id 노출** — `MainViewModel.cs`

```csharp
    public int? CurrentNoteId => _current?.Id;
```

- [ ] **Step 2: 이미지 붙여넣기(Ctrl+V) 구현** — `MainWindow.xaml.cs`의 `BodyEditor_PreviewKeyDown` stub 교체

```csharp
    private void BodyEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+V 이고 클립보드에 이미지가 있으면 가로채 첨부로 저장 후 마크다운 참조 삽입.
        bool ctrlV = e.Key == System.Windows.Input.Key.V
                     && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        if (!ctrlV) return;
        if (sender is not System.Windows.Controls.TextBox tb) return;
        if (ViewModel.CurrentNoteId is not int noteId) return;
        if (!System.Windows.Clipboard.ContainsImage()) return;   // 텍스트면 기본 동작 유지

        try
        {
            var src = System.Windows.Clipboard.GetImage();
            if (src is null) return;
            var bytes = EncodePng(src);
            var att = AppServices.Resolve<Memoria.Core.Attachments.IAttachmentService>();
            var rel = att.SaveImage(noteId, bytes, "png");
            InsertAtCaret(tb, $"![]({rel})");
            e.Handled = true;   // 기본 이미지 붙여넣기 억제
        }
        catch { /* 저장 실패 시 본문 무변경 */ }
    }

    private static byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource src)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void InsertAtCaret(System.Windows.Controls.TextBox tb, string text)
    {
        int at = tb.SelectionStart, len = tb.SelectionLength;
        tb.Text = tb.Text.Remove(at, len).Insert(at, text);
        tb.CaretIndex = at + text.Length;
        tb.Focus();
    }
```

- [ ] **Step 3: 파일 삽입(🖼 버튼) 구현** — `OnInsertImageClick` stub 교체

```csharp
    private void OnInsertImageClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel.CurrentNoteId is not int noteId) return;
        if (FindBodyEditor() is not System.Windows.Controls.TextBox tb) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "이미지|*.png;*.jpg;*.jpeg;*.gif;*.bmp|모든 파일|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var att = AppServices.Resolve<Memoria.Core.Attachments.IAttachmentService>();
            var rel = att.SaveFile(noteId, dlg.FileName);
            InsertAtCaret(tb, $"![]({rel})");
        }
        catch { /* 실패 시 무변경 */ }
    }
```

- [ ] **Step 4: 빌드 확인**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null; dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
```
기대: 경고 0, 오류 0.

- [ ] **Step 5: 커밋**

```bash
git add src/Memoria.App/MainWindow.xaml.cs src/Memoria.App/ViewModels/MainViewModel.cs
git commit -m "feat(md): image paste (Ctrl+V) + file insert into markdown body

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## RM8: 통합 — 전체 빌드·테스트·퍼블리시 + GUI 체크리스트

**Files:** 없음(검증 단계)

- [ ] **Step 1: 전체 빌드 + 전체 테스트**

```bash
taskkill.exe /IM Memoria.exe /F 2>/dev/null
dotnet.exe build "Memoria.sln" -c Release 2>&1 | tail -6
dotnet.exe test "tests/Memoria.Tests" -c Release 2>&1 | tail -4
```
기대: 경고 0/오류 0, 실패 0 / 통과(기존 310 + 신규 ~10).

- [ ] **Step 2: 자체 포함 단일 파일 퍼블리시**

```bash
dotnet.exe publish "src/Memoria.App/Memoria.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish 2>&1 | tail -3
ls -la --time-style=+%H:%M publish/Memoria.exe
```

- [ ] **Step 3: 사용자 GUI 검증 요청** — 아래 체크리스트로 `publish/Memoria.exe` 실행 검증:
  1. **새 메모** → 편집 모드로 열림, 툴바 표시. `# 제목`, `- 목록`, `**굵게**` 입력 → 👁 미리보기 전환 시 렌더 확인.
  2. **토글** 미리보기 ↔ 편집 왕복.
  3. **툴바** 버튼(B/I/H/•/1./🔗)이 선택 텍스트를 감싸거나 삽입.
  4. **이미지 붙여넣기**: 스크린샷 복사 → 편집 본문에 Ctrl+V → `![](attachments/...)` 삽입 + 미리보기에서 이미지 표시.
  5. **이미지 파일 삽입**: 🖼 버튼 → 파일 선택 → 참조 삽입 + 렌더.
  6. **기존 메모 무변경**: 이전에 만든 plain 메모는 그대로(마크다운 해석 안 함) + "마크다운으로 전환" 버튼 노출, 눌러 전환 확인.
  7. **목록 제목**: markdown 메모의 첫 줄 `# 제목`이 목록에 "제목"으로 표시.
  8. **검색**: 마크다운 본문 단어가 검색됨.
  9. **다크/라이트**: 렌더 텍스트·링크·코드 배경 대비, 하양 위 하양 없음.
  10. **영구삭제**: markdown+이미지 메모를 휴지통에서 영구삭제 → `attachments/{noteId}` 폴더 삭제 확인.

- [ ] **Step 4: (사용자 통과 후) finishing-a-development-branch로 병합 + v0.3.0 릴리스**
  - `superpowers:finishing-a-development-branch` 스킬 → master 병합 → `v0.3.0` 태그 푸시(git.exe) → Actions가 릴리스 자동 게시.

---

## Self-Review (작성자 점검 결과)

- **스펙 커버리지**: §4.1 저장/마이그레이션→RM1, body_format 왕복→RM2, §4.6 제목/검색·복구 무손상→RM3(+검색은 스키마 무변경으로 자동 충족), §4.5 첨부→RM4/RM7, §4.4 렌더→RM5, §4.2/4.3 렌더규칙·에디터/토글/전환→RM6, 테스트/검증→RM8. 전 항목 태스크 매핑됨.
- **플레이스홀더**: RM6 Step 8의 stub는 의도된 임시(같은 브랜치 RM7에서 즉시 구현) — 명시함. 그 외 TBD 없음.
- **타입 일관성**: `BodyFormat`(string), `IAttachmentService.SaveImage/SaveFile/ResolveToAbsolute/DeleteForNote`, `IMarkdownRenderer.Render(string?)`, VM `IsPreviewMode/BodyFormat/ShowSource/ShowPreview/ShowToolbar/IsMarkdown`, 커맨드 `TogglePreviewCommand/ConvertToMarkdownCommand`, `CurrentNoteId` — 태스크 간 명칭 일치 확인.
- **주의**: RM6 Step 8 stub(`OnInsertImageClick`/`BodyEditor_PreviewKeyDown`)는 RM7에서 구현되므로 두 태스크를 연속 실행. Markdig 0.38.0 복원 실패 시 최신 안정으로 교체. `AppServices.Resolve<T>()` 서비스 로케이터가 첨부/렌더러를 해석하려면 RM4/RM5의 DI 등록이 선행돼야 함(태스크 순서 준수).
