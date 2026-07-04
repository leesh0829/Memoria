# 구글 시트 주간보고 연동 — 설계

- 날짜: 2026-07-04
- 상태: 승인 대기 → 구현 계획 전 검토
- 대상 버전: v0.4.0 (phase 2 세 번째 서브프로젝트)
- 선행: v0.3.0 (마크다운 리치텍스트+이미지) 배포 완료

## 1. 배경 · 목표

주간보고의 원천 데이터를 사용자의 **구글 드라이브 구글 시트(일일 작업기록)**에서 **읽기 전용**으로 가져와 기존 주간보고(포맷 A/B)를 생성한다. 사용자는 이미 구글 시트에 일일 업무를 기록하므로, 앱은 그 시트를 읽어 주간보고를 만든다(앱 체크리스트 입력 불필요). **시트는 절대 수정하지 않는다.**

이 기능은 앱의 **첫 외부 네트워크 I/O**다(지금까지 순수 로컬 SQLite). 위험(네트워크/인증)은 얇은 어댑터로 격리하고, 가치 있는 파싱 로직은 순수·테스트 가능하게 둔다.

## 2. 결정 요약

- **인증 = 서비스 계정**: 구글 클라우드 서비스계정 JSON 키로 인증. 브라우저 동의 플로우·토큰 갱신 UI 없음. 사용자가 시트를 서비스계정 이메일과 '뷰어'로 공유.
- **구글 접근 = 공식 라이브러리**: `Google.Apis.Sheets.v4` + `Google.Apis.Auth`. 서비스계정 JWT·토큰·재시도를 라이브러리가 처리. 우리 코드는 "시트ID+범위 → 셀 격자"만 담당.
- **분리(시임)**: 파싱(격자→주간 업무/이슈)은 **Core 순수 함수(테스트 가능)**, 구글 fetch는 **App 어댑터(네트워크, 빌드+수동 검증)**.
- **비파괴 통합**: 기존 체크리스트 기반 주간보고 생성은 유지하고, 구글 시트를 **소스로 추가**. 시트 데이터는 기존 분류기·렌더러·저장 경로를 그대로 재사용.

## 3. 확인된 시트 구조 (원천)

파일 예: `유지보수 및 개발 요청 내역.xlsx`(구글 시트), 탭 **`일자 작업내역`**(3개 탭 중 하나).
- **1행 = 헤더**: `일자` / `작업내역` / `특이사항`.
- **2행부터 = 하루당 한 행**:
  - **A열 일자**: `YYYY.MM.DD (요일)` (예 `2025.09.19 (금)`).
  - **B열 작업내역**: 한 셀 안에 여러 업무가 `1. …\n2. …` 줄바꿈+번호로 나열.
  - **C열 특이사항(=이슈)**: B와 동일 구조. 빈 셀 존재.
- **완료여부 열 없음**(전부 '완료' 취급), **고객사 열 없음**(키워드 분류기로 자동 분류).

## 4. 상세 설계

### 4.1 파싱 규칙 (`SheetWorkParser`, Core 순수)
입력: 셀 격자 `IReadOnlyList<IReadOnlyList<string>>`, 대상 주 `monday`/`friday`(DateOnly). 출력: `ParsedWeek(IReadOnlyList<string> Tasks, IReadOnlyList<string> Issues)`.
- 1행(헤더) 스킵.
- 각 데이터 행:
  - A열 → `YYYY.MM.DD` 파싱(선행/후행 공백 및 `(요일)` 무시). 파싱 실패 또는 빈 날짜 → **그 행 건너뜀**(계속 진행).
  - 날짜가 `[monday, friday]` 밖 → 그 행 제외.
  - B열 → `\n`으로 분리, 각 줄 `Trim` 후 선행 번호 `^\s*\d+\s*[.,]\s*` 제거. 빈 줄 무시. → 각 줄이 업무 1건.
  - C열 → 동일 처리 → 각 줄이 이슈 1건. 빈/공백 셀이면 이슈 없음.
- 주 전체의 업무 줄을 순서대로 모아 `Tasks`, 이슈 줄을 모아 `Issues`.
- **분류·완료여부는 파서 책임 아님**(순수 텍스트 추출만). 분류는 §4.3에서.

### 4.2 구글 접근 (`ISpreadsheetReader` / `GoogleSheetReader`)
- **Core 인터페이스**(구글 의존성 없음): `Task<IReadOnlyList<IReadOnlyList<string>>> ReadRowsAsync(string sheetId, string tabName, CancellationToken ct = default)`. 셀 격자 반환. 인증/네트워크 오류는 예외로 던짐(호출자가 처리).
- **App 구현 `GoogleSheetReader`**(Google.Apis 사용):
  - `GoogleCredential.FromFile(jsonPath).CreateScoped(SheetsService.Scope.SpreadsheetsReadonly)`로 서비스계정 인증.
  - `new SheetsService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "Memoria" })`.
  - `spreadsheets.values.get(sheetId, "{tab}!A:C")` (async) → `IList<IList<object>>` → `string` 격자로 변환(null 셀 → "").
  - **읽기 전용 스코프만** 사용. 시트 쓰기 API 미사용.
- 어댑터는 얇게: 인증·fetch·격자 변환만. 파싱/분류/렌더는 하지 않음.

### 4.3 주간보고 통합
- 파싱 결과(`Tasks`/`Issues` 텍스트 목록)를 **기존 분류·렌더 경로에 연결**:
  - 각 업무 텍스트 → 기존 `ClientClassifier`로 고객사 지정(수동분류 없음) → `ReportTask(Text, ClientId, Done=true)`.
  - 각 이슈 텍스트 → `ReportIssue(Text)`.
  - 기존 `WeeklyReportRenderer.Render(model, options)`로 포맷 A/B 동일 출력.
- `WeeklyReportService`에 **텍스트 목록 기반 빌드 경로**를 추가(체크리스트 경로와 병존). 옵션(`ReportRenderOptions`)·클라이언트 목록·렌더는 기존 그대로.
- 생성된 보고서는 기존과 동일하게 `Note(Type=WeeklyReport, ReportFormat, ReportWeekStart)`로 저장(`FindWeeklyReport` 중복 처리 동일).
- **UI(WeeklyReportView)**: **소스 선택**(체크리스트 / 구글 시트) 추가. 구글 설정이 완료돼 있으면 기본=구글 시트.
- **비동기 범위 최소화**: **네트워크 fetch(`ReadRowsAsync`)만 async**이고 그 뒤 파싱·분류·렌더·저장은 기존과 동일하게 **동기**. `GenerateFromSheetCommand`가 `await`로 격자를 받은 뒤 나머지는 동기 호출. 기존 체크리스트 생성 경로는 손대지 않음.
- **분류·렌더 재사용(중복 금지)**: 새 빌드 경로는 기존 `ClientClassifier`·`WeeklyReportRenderer`·저장 로직을 **그대로 호출**한다. 분류/렌더 로직을 복제하지 않는다.

### 4.4 설정
- 새 설정 키(SQLite `settings`):
  - `google.serviceAccountJsonPath` — 서비스계정 JSON 키 **파일 경로**(키 내용은 DB에 복사하지 않음).
  - `google.sheetId` — 스프레드시트 ID(시트 URL의 `/d/{ID}/` 부분).
  - `google.sheetTabName` — 기본 `일자 작업내역`.
- 설정 창에 구글 연동 섹션 추가(경로 선택·시트ID·탭명 입력). 미설정 시 소스 선택에서 구글 시트 비활성/안내.
- 사용자 일회성 준비: 구글 클라우드에서 서비스계정+JSON키 생성 → 시트를 서비스계정 이메일과 '뷰어' 공유.

### 4.5 오류 처리
- 인증 실패(JSON 없음/형식 오류), 시트 미공유(403), 시트/탭 없음(404), 네트워크 실패, 빈 주 → **크래시 없이** 사용자에게 명확한 메시지(예: "시트를 서비스계정과 공유했는지 확인").
- 부분 실패(날짜 파싱 안 되는 행)는 그 행만 건너뛰고 나머지 계속.
- 취소(CancellationToken) 지원은 선택(장시간 fetch 대비). MVP는 단순 async + try/catch.

## 5. 컴포넌트 · 인터페이스

| 컴포넌트 | 계층 | 책임 | 의존 |
|---|---|---|---|
| `SheetWorkParser` | Core | 격자 → 주간 (Tasks, Issues) 텍스트 (순수, 테스트 가능) | — |
| `ISpreadsheetReader` | Core | 격자 fetch 계약(async) | — |
| `GoogleSheetReader` | App | 서비스계정 인증 + Sheets API fetch → 격자 | Google.Apis.Sheets.v4/Auth |
| `WeeklyReportService`(확장) | Core | 텍스트 목록 → 분류 → ReportModel(기존 Render 재사용) | ClientClassifier |
| `WeeklyReportViewModel`(확장) | App | 소스 선택 + async GenerateFromSheet | 위 서비스/리더 |
| 설정 키 + 설정 UI | App/Core | google.* 설정 저장·편집 | ISettingsRepository |

각 유닛은 단일 책임 + 명확한 인터페이스. 특히 리더는 인터페이스 뒤에 두어, 테스트에서 가짜 격자를 주입하고 파서·통합 로직을 네트워크 없이 검증한다.

## 6. 데이터 흐름
1. 사용자가 주간보고 뷰에서 소스=구글 시트 + 주 선택 → `GenerateFromSheetCommand`.
2. 설정에서 `sheetId`/`tabName`/`jsonPath` 로드 → `GoogleSheetReader.ReadRowsAsync` → 셀 격자.
3. `SheetWorkParser.Parse(grid, monday, friday)` → (Tasks, Issues) 텍스트.
4. `WeeklyReportService`가 각 업무 텍스트를 분류 → ReportTask/ReportIssue → `Render` → 보고서 텍스트.
5. Note(WeeklyReport)로 저장(기존 경로) + 에디터에 표시.

## 7. 테스트 전략
- **자동(Core, WSL→dotnet.exe)**:
  - `SheetWorkParser`: 날짜 파싱(`YYYY.MM.DD (요일)`), 주 필터(경계 포함/제외), 줄 분리 + 번호 제거(`1. `/`2, `), 빈 이슈셀, 헤더 스킵, 잘못된/빈 날짜 행 스킵, 여러 날 누적 순서.
  - 통합 빌드 경로: 텍스트 목록 → 분류(고객사 키워드) → 렌더(A/B) 출력 검증(가짜 격자 + 기존 렌더러).
  - `ISpreadsheetReader`는 테스트에서 가짜 격자 주입.
- **수동(Windows GUI)**: 실제 서비스계정 JSON + 공유된 시트로 fetch→생성, 오류 케이스(미공유/경로오류/네트워크), 포맷 A/B 출력.
- 목표: 빌드 경고 0, 기존 329 + 신규 테스트 그린. 네트워크 fetch는 자동 스위트에 넣지 않음.

## 8. 비목표 (후속)
- 시트 쓰기/양방향 동기화, 다중 탭·다중 시트 병합, 스케줄 자동 fetch.
- OAuth 사용자 동의 플로우(서비스계정으로 대체).
- .xlsx 파일 파싱(구글 시트 네이티브만).
- 완료여부·고객사 열 지원(시트에 없음), 열 매핑 커스터마이즈(A/B/C 고정).
