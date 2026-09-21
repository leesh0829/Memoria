# Memoria 작업 지침

## 필수 로컬 설치 명령

- 코드 변경 후 사용자에게 긴 `dotnet` 명령을 조합하거나 복사하게 하지 않는다.
- 관리자 PowerShell에서 아래 저장소 스크립트 한 줄만 실행하도록 안내한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install-Memoria.ps1
```

- 이 스크립트가 테스트, self-contained 단일 EXE 게시, 실행 프로세스 종료, 설치 파일 교체, SHA-256/버전 검증, 시작프로그램 바로가기 갱신 및 실행 확인을 모두 담당한다.
- 수동 명령은 스크립트 자체가 실행되지 않을 때의 진단용으로만 제공한다.

## Windows 빌드·배포 안내

- 이 프로젝트는 `net9.0-windows` WPF 앱이므로 빌드, 테스트, 게시 및 실행 검증은 Windows PowerShell에서 수행한다.
- 코드 변경을 마친 뒤 사용자에게 명령을 직접 조합하게 하지 말고 위 설치 스크립트 실행 명령을 제공한다.
- 일반 `dotnet build` 산출물의 `Memoria.exe`만 복사하면 실행되지 않는다. 설치용 파일은 반드시 self-contained 단일 파일로 `dotnet publish`한다.
- 로컬 설치 위치는 `C:\Program Files (x86)\Memoria\Memoria.exe`이며, 시작프로그램 바로가기는 이 파일을 대상으로 한다.
- 수동 게시가 필요하면 인수를 PowerShell 배열로 전달하여 줄바꿈 때문에 `-p:` 또는 `-o`가 별도 명령으로 실행되지 않게 한다.
- 실행 중인 앱이 설치 파일을 잠그므로 교체 전에 프로세스를 강제 종료하고 `Wait-Process`로 종료 완료를 기다린다.
- 설치 후 원본과 대상의 SHA-256 해시, 제품 버전, 실행 프로세스 경로를 확인한다.

### 1. 테스트

```powershell
cd "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria"
Get-Process Memoria -ErrorAction SilentlyContinue | Stop-Process -Force
Wait-Process -Name Memoria -Timeout 10 -ErrorAction SilentlyContinue
dotnet test .\Memoria.sln
```

### 2. 최신 단일 실행 파일 게시

다음 게시 명령은 반드시 한 줄 그대로 제공한다.

```powershell
dotnet publish .\src\Memoria.App\Memoria.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -o .\publish
```

성공 산출물은 다음 파일이다.

```text
C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\publish\Memoria.exe
```

### 3. 설치 파일 교체 및 검증

관리자 PowerShell에서 실행하도록 안내한다.

```powershell
$source = "C:\Users\adelie\Desktop\ToyProject\15_Memoria\1_PROJECT_FILE\Memoria\publish\Memoria.exe"
$target = "C:\Program Files (x86)\Memoria\Memoria.exe"
Get-Process Memoria -ErrorAction SilentlyContinue | Stop-Process -Force
Wait-Process -Name Memoria -Timeout 10 -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
Copy-Item $source $target -Force
$sourceHash = (Get-FileHash $source).Hash
$targetHash = (Get-FileHash $target).Hash
[PSCustomObject]@{ SourceHash = $sourceHash; TargetHash = $targetHash; Match = ($sourceHash -eq $targetHash); Version = (Get-Item $target).VersionInfo.ProductVersion }
Start-Process $target
Start-Sleep -Seconds 3
Get-Process Memoria | Select-Object Id, ProcessName, Path
```

성공 조건은 다음과 같다.

- 테스트 실패 `0`
- 게시 성공
- `Match`가 `True`
- `Version`이 이번에 올린 버전과 일치
- 실행 경로가 `C:\Program Files (x86)\Memoria\Memoria.exe`

### 4. 시작프로그램 바로가기

설치 경로가 바뀌지 않았다면 기존 바로가기를 다시 만들지 않는다. 없거나 잘못된 경우에만 다음 명령을 제공한다.

```powershell
$startup = [Environment]::GetFolderPath("Startup")
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $startup "Memoria.lnk"))
$shortcut.TargetPath = "C:\Program Files (x86)\Memoria\Memoria.exe"
$shortcut.WorkingDirectory = "C:\Program Files (x86)\Memoria"
$shortcut.IconLocation = "C:\Program Files (x86)\Memoria\Memoria.exe,0"
$shortcut.Save()
```
