# Memoria

> 저장하지 않아도 사라지지 않는 빠르게 켜는 메모장 + 그룹/날짜 정리 + 매일 체크리스트 → 금요일 주간보고 자동 생성. 네이티브 Windows 데스크톱 앱.

## 소개
Memoria는 Windows 메모장의 가벼움과 즉시성을 유지하면서, "저장" 행위를 없애고(자동 영속화) 메모를 그룹·날짜로 정리하며, 일일 업무일지를 주간보고(양식 2종)로 자동 생성하는 앱입니다. .NET 9 / WPF / SQLite(WAL, FTS5)로 구현되었습니다.

## 주요 기능
- 일반 메모(plain): 입력 즉시 디바운스 자동 저장(저장 버튼 없음).
- 체크리스트(일일 업무일지): 할 일(취소선) + 이슈, 고객사 자동 태깅 + 수동 교정.
- 주간보고 자동 생성: 양식 A(할일/이슈 순), 양식 B(고객사별 분류 + 제목줄), 클립보드 복사.
- 그룹(분류) 트리, (미분류) 가상 노드, 시스템 그룹(일일업무일지/주간보고).
- 전문검색(FTS5): 제목 + 본문 + 체크리스트 항목.
- 휴지통(소프트 삭제) + 복원/Undo.
- 전역 단축키 `Ctrl+Alt+N`, 트레이 상주, 자동시작, 단일 인스턴스.
- 테마: 라이트/다크/시스템 모드 + 강조색 + 프리셋 팔레트.

## 스크린샷
> 스크린샷은 첫 릴리스 후 추가 예정입니다.

- 메인 윈도우: `docs/images/main-window.png` (TBD)
- 주간보고 생성: `docs/images/weekly-report.png` (TBD)
- 테마 전환: `docs/images/theme.png` (TBD)

## 설치
1. [Releases](../../releases) 페이지에서 최신 `Memoria.exe`를 내려받습니다.
2. 단일 실행 파일이므로 별도 설치 과정이 없습니다. 원하는 폴더에 두고 실행하세요.
3. .NET 런타임 사전 설치 불필요(self-contained, win-x64).

## 실행
- `Memoria.exe`를 더블 클릭하면 트레이에 상주합니다.
- 어디서든 `Ctrl+Alt+N`으로 새 메모를 즉시 생성합니다.
- 트레이 아이콘 좌클릭으로 메인 창 표시/숨김을 토글합니다.

## 빌드
WPF는 Linux 네이티브 `dotnet`으로 빌드할 수 없습니다. **Windows .NET 9 SDK(`dotnet.exe`)** 로 빌드하세요.

WSL에서 호출(Windows 절대경로 사용):
```bash
dotnet.exe build "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\Memoria.sln"
dotnet.exe test  "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled\tests\Memoria.Tests"
```

Windows PowerShell에서 단일 exe 퍼블리시:
```powershell
cd "C:\Users\adelie\Desktop\ToyProject\15_Untitled\1_PROJECT_FILE\Untitled"
dotnet publish src\Memoria.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false
```
결과: `...\publish\Memoria.exe` 단일 실행 파일.

> 주의: WPF는 트리밍을 지원하지 않으므로 `PublishTrimmed`를 절대 사용하지 않습니다. 콜드 스타트 비용 때문에 `EnableCompressionInSingleFile`도 사용하지 않습니다.

## 단축키
| 동작 | 단축키 |
|---|---|
| 새 메모(전역) | `Ctrl+Alt+N` |
| 메인 창 표시/숨김 | 트레이 아이콘 좌클릭 |

## 데이터 위치
- 데이터베이스: `%LOCALAPPDATA%\Memoria\memoria.db` (SQLite, WAL 모드)
- 백업: `%LOCALAPPDATA%\Memoria\backups\`
- 크래시 복구 저널: `%LOCALAPPDATA%\Memoria\recovery\`

로밍(`%APPDATA%`)·네트워크 경로는 WAL 공유메모리(-shm)와 호환되지 않아 사용하지 않습니다.

## 문서
- [아키텍처](docs/architecture.md)
- [주간보고 양식 규칙](docs/weekly-report-format.md)
- [사용자 가이드(한글)](docs/user-guide.md)
- [변경 이력](CHANGELOG.md)

## 라이선스
TBD
