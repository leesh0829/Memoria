# 주간보고 양식 C 설계

날짜: 2026-08-14
상태: 승인됨

## 배경

주간보고는 현재 양식 A(업무/이슈 머리글 + 고객사 접두)와 양식 B(고객사별 섹션)를 지원한다.
사내 주간 실적 보고용으로 세 번째 양식이 필요하다. 양식 A·B와 달리 **집계 기간이 월~금이 아니라
전주 금요일 ~ 해당 주 목요일**이며, 세부 설명은 사용자가 직접 채워 넣는 빈 머릿말 줄만 제공한다.

## 출력 형식

```
[ 주간 실적 ]

== 이승현
- 라인 셋업 점검
    o
- 미팅 정리
    o

[ 차주 계획 ]
```

규칙:

- `[ 주간 실적 ]` 머리글, 빈 줄, `== {보고자 이름}` 순서.
- 업무(task) 한 건마다 `- {업무 텍스트}` 줄과 그 아래 `    o` 줄(공백 4칸 들여쓰기)을 출력한다.
  `o` 뒤는 비워 두며, 사용자가 직접 설명을 적는다.
- 마지막에 빈 줄 하나와 `[ 차주 계획 ]` 머리글만 출력한다. 계획 내용은 사용자가 직접 채운다.
- **이슈(issue) 항목은 출력하지 않는다.** 양식 C는 업무 실적만 다룬다.
- **선택한 고객사의 업무만 출력한다.** 기본값은 `SLD 자율형공장` 하나. 미분류 업무는 필터가 걸린 상태에서 제외된다.
- **고객사명 접두를 붙이지 않는다.** 체크리스트 원문을 그대로 쓴다(양식 A와 다른 점).
- `완료 항목만 포함(includeDoneOnly)` 설정은 양식 A·B와 동일하게 적용한다.
- 업무가 한 건도 없으면 `== 이름` 아래에 아무 줄도 넣지 않는다(머리글 골격은 유지).
- 들여쓰기는 공백 4칸으로 고정한다. 공통 `report.indent`(기본 탭)를 쓰지 않는 이유는
  붙여넣는 문서의 탭 처리 방식에 따라 정렬이 깨질 수 있어서다.

## 기간 계산

`IWeekCalculator`에 커스텀 범위 계산을 추가한다.

```csharp
(DateOnly Start, DateOnly End) GetCustomRange(DateOnly anyDate, DayOfWeek startDay, DayOfWeek endDay);
```

규칙:

1. 종료일 = `anyDate`가 속한 주(월요일 기준)의 `endDay` 요일 날짜.
2. 시작일 = 종료일에서 거슬러 올라가 처음 만나는 `startDay` 요일 날짜.
   `diff = ((int)endDay - (int)startDay + 7) % 7`, `diff == 0`이면 `diff = 7`, `start = end.AddDays(-diff)`.

기본값(시작=금, 종료=목)이면 전주 금요일 ~ 해당 주 목요일이 된다.
시작=월, 종료=금으로 바꾸면 양식 A·B와 동일한 월~금이 된다. 시작 요일과 종료 요일이 같으면 7일 구간이 된다.

## 설정

`SettingsKeys`에 다음을 추가한다. 보고자 이름은 기존 `report.reporterName`을 재사용한다.

| 키 | 기본값 | UI |
| --- | --- | --- |
| `report.formatC.startDay` | `Friday` | 요일 콤보박스 |
| `report.formatC.endDay` | `Thursday` | 요일 콤보박스 |
| `report.formatC.titleHeader` | `[ 주간 실적 ]` | 텍스트 |
| `report.formatC.planHeader` | `[ 차주 계획 ]` | 텍스트 |
| `report.formatC.detailMarker` | `o` | 텍스트 |
| `report.formatC.clientIds` | (미설정 → `SLD 자율형공장`) | 고객사 체크 목록 |

포함 고객사는 client id를 쉼표로 이어 저장한다(이름은 개명될 수 있으므로). 해석 규칙:

- 키 없음 → `SLD 자율형공장` 이름으로 찾아 그 id 하나. 그 이름이 없으면 필터 없음(전부 출력).
- 빈 문자열 → 선택된 고객사 없음(업무가 출력되지 않음).
- 값 있음 → 현재 존재하는 고객사 id만 남긴다.

설정 창 `주간보고` 탭에 양식 B 항목 아래로 배치한다. 요일 값은 `DayOfWeek` 열거형 이름으로 저장하고,
파싱 실패 시 기본값으로 폴백한다.

## 화면·동작

- `WeeklyReportView`에 `양식 C` 라디오 버튼을 추가한다.
- `생성` / `다시 생성` / `복사` / `구글 시트에서 생성` 네 동작 모두 양식 A·B와 동일한 경로를 탄다.
- 주차 범위 라벨(`WeekRangeLabel`)과 `WeekStart`/`WeekEnd`는 선택된 양식에 따라 달라진다.
  A·B는 월~금, C는 설정된 커스텀 범위.
- 구글 시트 파싱도 같은 범위를 쓰므로, 양식 C에서는 금~목 행이 집계된다.
- 미분류 경고 배너 문구는 양식 B를 언급하므로 그대로 둔다(양식 C는 고객사 분류를 쓰지 않는다).

## 데이터 저장

- `report_week_start`는 A·B·C 모두 **선택 날짜가 속한 주의 월요일**을 앵커로 저장한다.
  양식 C의 표시 범위(금~목)를 저장하면 노트를 다시 열 때 `SelectedDate`가 금요일이 되어
  범위가 한 주 밀린다. 앵커를 월요일로 통일하면 재계산이 정확하다.
- `(report_week_start, report_format)` 조합으로 행이 구분되므로 A·B·C가 충돌하지 않는다.
- `notes.report_format`은 CHECK 제약이 없는 TEXT 컬럼이므로 **DB 마이그레이션이 필요 없다.**
  `DapperConfig.ReportFormatKindHandler`와 `NoteRepository.ReportFormatToString`에 `"C"` 케이스만 추가한다.

## 테스트

- `WeekCalculatorTests`: 금~목 기본, 월~금, 시작·종료 요일 동일(7일), 주 경계(일요일 입력).
- `WeeklyReportRendererFormatCTests`: 골든 텍스트, 업무 0건, `includeDoneOnly`, 이슈 무시,
  고객사 접두 없음, 커스텀 머리글·머릿말.
- `WeeklyReportViewModelTests`: 양식 C 선택 시 범위 라벨/`WeekStart`/`WeekEnd`, 월요일 앵커 저장,
  양식 전환 시 재렌더.
- `NoteRepository` 왕복: 양식 C 저장 후 `FindWeeklyReport`로 조회.

## 배포

버전 0.8.2 → 0.9.0(기능 추가). `Memoria.App.csproj`의 `<Version>` 갱신 후 master 병합,
`v0.9.0` 태그 푸시로 GitHub Actions 릴리스를 트리거한다.
