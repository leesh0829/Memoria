# 마크다운 노트 3-모드 (읽기/편집/마크다운) — 설계

- 날짜: 2026-07-09
- 상태: 승인 대기 → 구현 계획 전 검토
- 대상 버전: v0.6.0
- 선행: v0.3.0(마크다운 리치텍스트+이미지), v0.5.0(사이드바 UX)

## 1. 배경 · 목표

v0.3.0의 마크다운 노트는 내용이 있으면 **열 때 자동으로 마크다운 렌더(미리보기)** 되는데, 사용자 피드백:
1. 렌더 스타일이 별로다 — **폰트/가독성**(원인: `MarkdownRenderer`가 FontSize만 지정, **FontFamily 미지정** → FlowDocument 기본 세리프 폰트).
2. 마크다운 렌더가 **기본값(자동)인 게 싫다** → 기본은 마크다운 서식 OFF, **버튼을 눌러야** 렌더.
3. **이미지는 항상 보여야** 한다(마크다운 서식 OFF일 때도).

목표: 마크다운 노트를 **읽기/편집/마크다운 3-모드**로 만들고, **열 때 기본=읽기**(서식 없는 원본 텍스트 + 이미지만 렌더)로 한다. 폰트를 앱 기본 글꼴로 개선한다.

## 2. 결정 요약 (브레인스토밍)

- **3-모드**: 읽기(Read) / 편집(Edit) / 마크다운(Rendered). 상단 세그먼트 버튼 `[읽기] [편집] [마크다운]`.
- **열 때 기본 = 읽기**(내용 있는 마크다운 노트). **빈/새 노트 = 편집**(바로 타이핑).
- **읽기 뷰** = 원본 텍스트를 서식 없이 그대로(`# 제목`, `**굵게**` 문자 그대로) + **이미지 참조만 실제 이미지로 렌더**. 읽기 전용.
- **폰트 개선** = 렌더/읽기 FlowDocument에 앱 기본 글꼴 지정.
- **plain 노트**(비마크다운)는 기존처럼 편집 TextBox만(3-모드 미적용).

## 3. 상세 설계

### 3.1 상태 모델 (VM)
- 기존 `bool IsPreviewMode` → **`enum MarkdownViewMode { Read, Edit, Rendered }`** + `[ObservableProperty] MarkdownViewMode viewMode`.
- 계산 속성:
  - `IsMarkdown => BodyFormat == "markdown"` (기존)
  - `ShowRead => IsMarkdown && ViewMode == Read`
  - `ShowRendered => IsMarkdown && ViewMode == Rendered`
  - `ShowEdit => !IsMarkdown || ViewMode == Edit` (plain 노트는 항상 편집)
  - `ShowToolbar => IsMarkdown && ViewMode == Edit`
- 커맨드: `SetReadModeCommand` / `SetEditModeCommand` / `SetRenderedModeCommand`(각각 ViewMode 설정). 기존 `TogglePreviewCommand` 제거/대체.
- `OpenNote`: 마크다운 노트면 `ViewMode = string.IsNullOrEmpty(note.Body) ? Edit : Read`. (빈 노트는 편집, 내용 있으면 읽기.)
- `ConvertToMarkdown`(plain→markdown): 변환 후 `ViewMode = Edit`.
- 새 노트(`NewPlainNote`, BodyFormat="markdown", 빈 본문): OpenNote 규칙에 따라 Edit로 열림.

### 3.2 읽기 뷰 분할 (Core 순수, 테스트 가능)
- `Memoria.Core.Text.MarkdownReadSegmenter.Segment(string? body) -> IReadOnlyList<ReadSegment>`.
  - `record ReadSegment(bool IsImage, string Value)` — 이미지면 Value=이미지 상대경로, 텍스트면 Value=리터럴 텍스트.
  - 규칙: 정규식 `!\[[^\]]*\]\(([^)]+)\)`로 본문을 이미지 참조 기준 분할. 이미지 사이/앞뒤 텍스트는 그대로 텍스트 세그먼트(마크다운 문법 문자 보존). 이미지 세그먼트는 경로만.
  - 빈 본문 → 빈 목록(또는 빈 텍스트 1개). 이미지 없음 → 텍스트 1개.
- 순수 함수라 단위 테스트로 검증(분할 개수/순서/경로 추출).

### 3.3 읽기 뷰 렌더 (App, WPF)
- `IMarkdownRenderer`에 **`FlowDocument RenderRead(string? body)`** 추가(또는 별도 리더).
  - `MarkdownReadSegmenter.Segment`로 세그먼트 → FlowDocument 구성: 텍스트 세그먼트는 리터럴(줄바꿈 보존) Paragraph/Run, 이미지 세그먼트는 `IAttachmentService.ResolveToAbsolute`로 `Image`(BlockUIContainer/InlineUIContainer). 읽기 전용.
  - 깨진 경로 → 대체 텍스트 `[이미지: 경로]`(크래시 없음). 전체 try/catch 폴백.
- 마크다운 서식(제목/굵게/목록)은 **적용하지 않음** — 텍스트 그대로.

### 3.4 마크다운 렌더 폰트 개선 (App, #1)
- `MarkdownRenderer.Render`(전체 렌더)와 `RenderRead`(읽기)의 FlowDocument에 **`FontFamily` 지정**(앱 기본 글꼴, 예: `new FontFamily("Segoe UI, 맑은 고딕")`) + 적절한 `LineHeight`/문단 간격.
- 테마 브러시 연동(Brush.Foreground 등)은 기존 유지.

### 3.5 뷰 (XAML) — Plain 에디터 DataTemplate
- 상단 모드 버튼: 기존 미리보기 토글 → **[읽기][편집][마크다운]** 3버튼(마크다운 노트일 때만 표시). plain 노트엔 "마크다운으로 전환" 버튼 유지.
- 본문 영역 3택1:
  - **읽기 뷰**: `FlowDocumentScrollViewer` + 읽기 렌더(신규) — `ShowRead`.
  - **편집 뷰**: 기존 소스 TextBox + 문법 툴바 — `ShowEdit`/`ShowToolbar`.
  - **마크다운 뷰**: 기존 `FlowDocumentScrollViewer` + 전체 렌더 — `ShowRendered`.
- `MarkdownPreviewBehavior`에 **렌더 모드 구분**(읽기 vs 전체) 추가: 첨부 속성 `RenderMode`(read/rendered) 또는 읽기 전용 두 번째 뷰어. 뷰어별로 해당 렌더 호출.

### 3.6 자동저장/저장/제목 영향 없음
- 편집은 기존 소스 TextBox·자동저장 경로 그대로(읽기/마크다운은 읽기 전용). body_format·검색·복구·목록 제목(NoteTitleResolver) 무변경.

## 4. 컴포넌트 · 인터페이스

| 컴포넌트 | 계층 | 변경 | 비고 |
|---|---|---|---|
| `MarkdownViewMode` enum | App | 신규(Read/Edit/Rendered) | |
| `MarkdownReadSegmenter` + `ReadSegment` | Core | 신규(순수, 테스트) | 텍스트/이미지 분할 |
| `IMarkdownRenderer.RenderRead` | App(WPF) | 신규 | 텍스트+이미지 FlowDocument |
| `MarkdownRenderer` | App(WPF) | FontFamily 지정(Render+RenderRead) | 폰트 개선 |
| `MainViewModel` | App | ViewMode + Show*/Set*Mode + OpenNote/Convert | IsPreviewMode 대체 |
| `MarkdownPreviewBehavior` | App | 읽기/전체 렌더 구분 | RenderMode 속성 |
| `MainWindow.xaml`(Plain 템플릿) | App | 3버튼 + 3뷰 | |

## 5. 오류 처리
- 읽기/마크다운 렌더 실패 → 원문 텍스트 폴백(크래시 금지). 깨진 이미지 → 대체 텍스트.
- 모드 전환 시 편집 중이던 내용은 자동저장이 이미 반영(읽기/마크다운 진입 전 저장 확정).

## 6. 테스트 전략
- **자동(Core)**: `MarkdownReadSegmenter` — 텍스트/이미지 분할(앞뒤/사이 텍스트, 다중 이미지, 이미지 없음, 빈 본문, alt 텍스트 무시하고 경로 추출).
- **자동(App VM)**: 모드 전환/기본값 — OpenNote(빈→Edit, 내용→Read), Set*Mode, ConvertToMarkdown→Edit, Show* 계산.
- **수동(Windows GUI)**: 읽기 뷰(텍스트 그대로+이미지 렌더), 폰트 개선, 3버튼 전환, 편집 저장, plain 노트 무변경, 다크/라이트.
- 목표: 빌드 경고 0, 기존 347 + 신규 테스트 그린. WPF 렌더는 GUI 검증.

## 7. 비목표 (후속)
- 읽기 뷰에서 마크다운 문법 문자 숨김(예: `#` 제거) — 이번엔 원본 그대로 표시.
- WYSIWYG 편집, 읽기 뷰 내 인라인 편집.
- plain 노트의 3-모드화.
