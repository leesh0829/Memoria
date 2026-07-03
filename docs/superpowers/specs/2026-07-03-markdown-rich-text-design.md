# 마크다운 리치 텍스트 + 이미지 첨부 — 설계

- 날짜: 2026-07-03
- 상태: 승인 대기 → 구현 계획 전 검토
- 대상 버전: v0.3.0 (phase 2 두 번째 서브프로젝트)
- 선행: v0.2.0 (그룹 중첩) 배포 완료

## 1. 배경 · 목표

일반(Plain) 메모에 **서식 위주**의 리치 텍스트(제목·목록·굵게/기울임 등 구조와 가독성)와 **이미지 첨부**를 추가한다. 사용자 주 용도는 "문서처럼 서식을 갖춘 업무 메모"이며 이미지는 가끔 삽입한다.

핵심 제약: 현재 아키텍처를 **깨지 않고** 확장한다.
- `notes.body`는 plain TEXT. FTS5 `notes_fts(title, body, items)`가 body를 색인.
- 자동저장(DebounceAutosaveService)·크래시복구(RecoveryJournal)가 `(Title, Body)` **문자열 스냅샷**을 사용.
- 목록 제목은 `NoteTitleResolver`가 body 첫 줄에서 추출.
- 기존 메모는 전부 plain 텍스트 → **무손실 호환** 필수.
- body를 쓰는 노트 타입은 **Plain 뿐**(Checklist·WeeklyReport는 body 미사용).

## 2. 결정 요약

- **접근 A — 마크다운 저장**: `body`는 마크다운 텍스트 그대로(TEXT 유지). WYSIWYG(RTF/XAML) 대신 마크다운을 택해 검색·복구·호환을 하나도 깨지 않는다.
- **토글 편집**: 기본 렌더 미리보기, `✎ 편집` 토글 시 마크다운 소스 편집. 한 뷰가 편집 열 전체 폭 사용.
- **이미지 = 디스크 파일 + 본문 참조**: `%LOCALAPPDATA%\Memoria\attachments\{noteId}\{guid}.ext`에 저장하고 본문엔 `![](상대경로)`. DB blob 미사용(DB 비대·백업부담 회피).
- **렌더러**: Markdig(파서) + WPF FlowDocument(브라우저/WebView2 의존성 없음).
- 적용 범위: **Plain 메모만**.

## 3. 범위

### 포함
- 마크다운 저장/렌더(제목·굵게·기울임·글머리/번호 목록·링크·이미지·코드·인용).
- 편집/미리보기 토글, 편집 모드 툴바(문법 삽입).
- 이미지 클립보드 붙여넣기 + 파일 삽입, 미리보기 렌더.
- 스키마 v2: `body_format` 컬럼, 기존 메모 `'plain'` 보존, 신규 Plain 메모 `'markdown'` 기본.
- plain↔markdown 전환 버튼.

### 제외 (후속 사이클)
- WYSIWYG(편집 중 서식 즉시 표시), 표 편집 UX, 수식.
- 첨부 고아(orphan) 자동 정리(스캐너), 이미지 리사이즈/썸네일.
- Checklist·WeeklyReport의 마크다운화.
- 이미지 외 파일(첨부 문서 등) 첨부.

## 4. 상세 설계

### 4.1 저장 & 스키마 (마이그레이션 v2)
- `DatabaseInitializer`의 `TargetVersion`을 2로 올리고 v2 스텝 추가:
  - `ALTER TABLE notes ADD COLUMN body_format TEXT NOT NULL DEFAULT 'plain';`
  - 기존 행은 DEFAULT로 자동 `'plain'`.
- 값 도메인: `'plain' | 'markdown'`. (문자열, 향후 확장 여지.)
- **컬럼 DEFAULT `'plain'`은 기존 행 마이그레이션 전용.** 신규 Plain 메모는 앱의 노트 생성 경로가 `body_format='markdown'`을 **명시적으로** 지정한다(DB 기본값에 의존하지 않음).
- FTS 스키마·트리거는 **변경 없음**(body는 계속 문자열이라 그대로 색인).

### 4.2 렌더 규칙
- `body_format == 'plain'`: 지금과 동일하게 **글자 그대로** 표시(마크다운 해석 안 함). 기존 메모가 `# 1.` 같은 문자를 포함해도 렌더가 바뀌지 않는다.
- `body_format == 'markdown'`: Markdig로 파싱해 FlowDocument로 렌더.
- **신규 Plain 메모는 `'markdown'`으로 생성**(기능 기본 ON). 기존 `'plain'` 메모는 편집화면의 **"마크다운으로 전환"** 명령으로 `'markdown'`으로 변경(본문은 그대로 두고 포맷만 전환, 즉시 저장).

### 4.3 에디터 (토글)
- MainViewModel(=Plain 에디터 컨텍스트)에 상태 추가:
  - `bool IsPreviewMode`(기본 true) — 미리보기/편집 전환.
  - `string BodyFormat` — 현재 메모의 포맷.
  - `bool IsMarkdown => BodyFormat == "markdown"`.
- **모드 초기값**: 본문이 비었거나 새 메모 → **편집 모드**로 열림. 내용 있으면 **미리보기**. (바로 타이핑 vs 바로 읽기.)
- **미리보기 모드**: FlowDocument 렌더(읽기 전용). `'plain'`이면 단순 텍스트.
- **편집 모드**: 마크다운 소스 TextBox(기존 EditorBody 바인딩 재사용, `UpdateSourceTrigger=PropertyChanged`).
- **툴바(편집 모드 상단)**: 굵게`**…**` · 기울임`*…*` · 제목`# ` · 글머리목록`- ` · 번호목록`1. ` · 링크`[](url)` · 이미지삽입. 각 버튼은 선택 영역을 감싸거나 커서 위치에 문법을 삽입(선택 유지).
- 제목(EditorTitle)·헤더(생성/수정일)·저장상태 표시는 두 모드 공통 유지.
- 미리보기 모드에서 `'plain'` 메모에는 "마크다운으로 전환" 버튼 노출.

### 4.4 마크다운 렌더링
- 파서: **Markdig** NuGet(공통 확장 활성: 목록, 링크, 강조, 코드, 인용, 표 정도).
- 렌더: **WPF FlowDocument**.
  - 1차 구현: `Markdig.Wpf`(Markdown→FlowDocument) 사용, `FlowDocumentScrollViewer`로 표시.
  - **테마 연동**: 뷰어 `Foreground={DynamicResource Brush.Foreground}`, 배경 `Brush.EditorBackground`; 링크/코드/인용 색은 `Brush.Accent`/`Brush.SecondaryForeground` 등으로 스타일 오버라이드.
  - **폴백**: `Markdig.Wpf`의 테마·스타일 제약이 크면 Markdig AST를 순회하는 **얇은 자체 FlowDocument 렌더러**로 대체(핵심 노드: Heading/Paragraph/Emphasis/List/Link/Image/CodeBlock/Quote). 인터페이스(§5)는 동일하게 유지해 교체 비용을 흡수.
- 렌더러는 `IMarkdownRenderer.Render(string markdown) -> FlowDocument` 형태의 **단일 진입점**으로 캡슐화(WPF 의존, App 계층).

### 4.5 이미지 첨부
- 경로: `AppPaths.AttachmentsDirectory = %LOCALAPPDATA%\Memoria\attachments`. 노트별 하위 폴더 `attachments/{noteId}/`.
- 파일명: `{guid}.{ext}`(png/jpg 등 원본 확장자, 클립보드는 png).
- **붙여넣기**: 편집 모드 소스 TextBox에서 클립보드에 이미지가 있으면 Ctrl+V 가로채기 → 파일 저장 → 커서 위치에 `![](attachments/{noteId}/{guid}.png)` 삽입. (텍스트 붙여넣기는 기본 동작.)
- **파일 삽입**: 툴바 이미지 버튼 → OpenFileDialog(이미지 필터) → attachments로 복사 → 참조 삽입.
- **렌더**: 미리보기에서 마크다운 이미지의 상대경로를 `DataDirectory` 기준 절대경로로 해석해 표시. 경로가 깨졌으면 대체 텍스트/플레이스홀더.
- **수명주기**: 소프트 삭제(휴지통)는 첨부 보존. **영구삭제(purge) 시 `attachments/{noteId}/` 폴더 삭제.** 고아 정리 스캐너는 후속.
- **서비스 분리(테스트 가능성)**:
  - `IAttachmentService`(**Core**, 파일시스템 + 경로 로직 → temp 디렉터리로 단위 테스트 가능) — `SaveImage(noteId, byte[] bytes, string ext) -> string relPath`, `SaveFile(noteId, srcPath) -> string relPath`, `ResolveToAbsolute(relPath) -> string`, `DeleteForNote(noteId)`.
  - **클립보드 이미지 추출은 App 코드비하인드**: WPF `Clipboard.GetImage()` → PNG 바이트로 인코딩 → `IAttachmentService.SaveImage(noteId, bytes, "png")` 호출. (WPF 의존부는 얇게, 로직은 Core.)

### 4.6 기존 시스템 연동 (무손상)
- **검색(FTS)**: body가 계속 문자열 → 그대로 동작. 원본 마크다운을 색인하므로 `**굵게**`의 "굵게"도 매치. `snippet()`에 `**` 등 기호가 섞일 수 있으나 허용. 스키마 변경 없음.
- **자동저장/크래시복구**: 스냅샷 `(Title, Body)` 문자열 → 변경 없음. `body_format`은 전환 시 **즉시 저장**(디바운스 밖의 소량 UPDATE). 복구 시 body는 마크다운 문자열 그대로 복원.
- **NoteTitleResolver**: `'markdown'` 메모는 첫 비어있지 않은 줄에서 선행 마크다운 기호(`#`, `>`, `-`, `*`, `1.`)를 벗겨 제목 표시. `'plain'`은 현행 유지. (순수 Core 로직 → 단위 테스트 가능.)

## 5. 컴포넌트 · 인터페이스

| 컴포넌트 | 계층 | 책임 | 의존 |
|---|---|---|---|
| `DatabaseInitializer` v2 | Core | `body_format` 컬럼 마이그레이션 | SQLite |
| `Note.BodyFormat` (모델 필드) | Core | 포맷 상태 | — |
| `INoteRepository` (기존) | Core | body_format 왕복(Create/Update/Get) | Dapper |
| `NoteTitleResolver` (확장) | Core | 마크다운 제목 추출 | — |
| `AppPaths.AttachmentsDirectory` | App | 첨부 루트 경로 | — |
| `IAttachmentService` | Core | 이미지 바이트 저장/경로 해석/노트 폴더 삭제(테스트 가능) | 파일시스템 |
| 클립보드 이미지 추출 | App(WPF) | `Clipboard.GetImage()`→PNG 바이트 | WPF Imaging |
| `IMarkdownRenderer` | App(WPF) | 마크다운→FlowDocument | Markdig(.Wpf) |
| `MainViewModel` (확장) | App | IsPreviewMode/BodyFormat/토글·툴바 커맨드/붙여넣기 | 위 서비스 |
| `MainWindow.xaml` (Plain DataTemplate) | App | 토글 UI·툴바·미리보기 뷰어 | — |

각 유닛은 단일 책임 + 명확한 인터페이스로 분리한다. 특히 렌더러는 인터페이스 뒤에 두어 `Markdig.Wpf`↔자체 렌더러 교체가 소비자에 영향 없게 한다.

## 6. 데이터 흐름
1. 메모 열기 → 리포지토리가 `body`, `body_format` 로드 → VM 초기화(모드 결정).
2. 미리보기 → `IMarkdownRenderer.Render(body)` → FlowDocument 표시(plain이면 텍스트).
3. 편집 → TextBox가 `EditorBody` 갱신 → 기존 OnContentChanged → 디바운스 자동저장 + 복구 저널(변경 없음).
4. 이미지 붙여넣기/삽입 → `IAttachmentService`가 파일 저장 → 마크다운 참조를 EditorBody에 삽입 → (3)의 저장 경로로 영속.
5. plain→markdown 전환 → `body_format` 즉시 UPDATE → 미리보기 재렌더.
6. 영구삭제 → 노트 purge 시 `IAttachmentService.DeleteForNote`.

## 7. 오류 처리
- 이미지 저장 실패(디스크/권한): 사용자에게 알리고 참조 삽입 안 함(본문 무변경).
- 렌더 실패(깨진 마크다운): 예외를 삼키고 원문 텍스트로 폴백 표시(앱 크래시 금지).
- 깨진 이미지 참조(파일 없음): 대체 텍스트/플레이스홀더로 렌더.
- 마이그레이션 실패: 기존 마이그레이션 러너 규약을 따름(트랜잭션, 실패 시 중단).

## 8. 테스트 전략
- **Core(자동, WSL→dotnet.exe)**:
  - v2 마이그레이션: 컬럼 추가, 기존 행 `'plain'`, user_version=2.
  - 리포지토리: `body_format` Create/Update/Get 왕복, 기본값.
  - `NoteTitleResolver`: 마크다운 선행기호 제거, plain 유지, 빈 본문.
  - `IAttachmentService`(경로 로직): 저장 경로 규칙, ResolveToAbsolute, DeleteForNote(폴더 삭제).
- **WPF(수동, Windows GUI)**: 마크다운 렌더 모양/테마 대비, 토글, 이미지 붙여넣기·삽입·렌더, 전환 버튼, 기존 plain 메모 무변경 확인.
- 목표: 빌드 경고 0, 기존 310 + 신규 테스트 그린.

## 9. 비목표
- 실시간 WYSIWYG, 협업/동기화, 이미지 편집, 첨부 버전관리.
- 마크다운 방언 완전 지원(GFM 전체) — 공통 서식 세트에 집중(YAGNI).
