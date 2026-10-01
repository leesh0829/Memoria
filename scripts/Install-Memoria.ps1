[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path ${env:ProgramFiles(x86)} "Memoria"),
    [switch]$SkipTests,
    [switch]$NoStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solution = Join-Path $repoRoot "Memoria.sln"
$project = Join-Path $repoRoot "src\Memoria.App\Memoria.App.csproj"
$publishDirectory = Join-Path $repoRoot "publish"
$source = Join-Path $publishDirectory "Memoria.exe"
$target = Join-Path $InstallDirectory "Memoria.exe"

function Wait-MemoriaExit([int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $remaining = @(Get-Process Memoria -ErrorAction SilentlyContinue | Where-Object {
            try { -not $_.HasExited } catch { $true }
        })
        if ($remaining.Count -eq 0) { return @() }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    return $remaining
}

function Test-InstallFileUnlocked {
    if (-not (Test-Path $target -PathType Leaf)) { return $true }
    try {
        $stream = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $stream.Dispose()
        return $true
    }
    catch { return $false }
}

function Stop-Memoria {
    $processes = @(Get-Process Memoria -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }

    $processes | Stop-Process -Force -ErrorAction SilentlyContinue
    $remaining = @(Wait-MemoriaExit 10)
    foreach ($process in $remaining) {
        # Stop-Process가 멎은 프로세스 트리를 정리하지 못한 경우 Windows taskkill로 한 번 더 종료한다.
        # 이미 종료된 자식에 대한 taskkill 오류는 무시하고 실제 프로세스/파일 잠금을 아래에서 재확인한다.
        & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null
    }
    # 수 GB 메모리를 사용한 프로세스는 종료 후 working set/pagefile 회수에 시간이 걸릴 수 있다.
    $remaining = @(Wait-MemoriaExit 30)
    if ($remaining.Count -gt 0) {
        $cimIds = @()
        $cimQuerySucceeded = $false
        try {
            $cimIds = @(Get-CimInstance Win32_Process -Filter "Name = 'Memoria.exe'" -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty ProcessId)
            $cimQuerySucceeded = $true
        }
        catch { }

        $liveIds = @($remaining | Where-Object { $cimIds -contains [uint32]($_.Id) } |
            Select-Object -ExpandProperty Id)
        if ($cimQuerySucceeded -and $liveIds.Count -eq 0 -and (Test-InstallFileUnlocked)) {
            Write-Warning "종료된 Memoria 프로세스 항목이 잠시 남아 있지만 실행 파일 잠금은 해제되었습니다. 설치를 계속합니다."
            return
        }

        $ids = ($remaining | Select-Object -ExpandProperty Id) -join ", "
        throw "Memoria 프로세스를 종료하지 못했습니다. Windows 재시작 후 다시 실행하세요. PID: $ids"
    }
}

function Invoke-DotNet([string[]]$Arguments, [string]$FailureMessage) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

$isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    throw "Program Files에 설치하려면 PowerShell을 관리자 권한으로 실행해야 합니다."
}

Push-Location $repoRoot
try {
    Stop-Memoria

    if (-not $SkipTests) {
        Invoke-DotNet -Arguments @("test", $solution) -FailureMessage "테스트 실패: 게시를 중단합니다."
    }

    $publishArguments = @(
        "publish", $project,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-o", $publishDirectory
    )
    Invoke-DotNet -Arguments $publishArguments -FailureMessage "게시 실패: 설치를 중단합니다."

    if (-not (Test-Path $source -PathType Leaf)) {
        throw "게시 파일을 찾을 수 없습니다: $source"
    }

    Stop-Memoria
    New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null

    $copied = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Copy-Item $source $target -Force
            $copied = $true
            break
        }
        catch {
            if ($attempt -eq 10) { throw }
            Stop-Memoria
            Start-Sleep -Milliseconds 300
        }
    }
    if (-not $copied) { throw "설치 파일을 교체하지 못했습니다: $target" }

    $sourceHash = (Get-FileHash $source -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        throw "설치 파일 해시가 게시 파일과 일치하지 않습니다."
    }

    $startup = [Environment]::GetFolderPath("Startup")
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $startup "Memoria.lnk"))
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $InstallDirectory
    $shortcut.IconLocation = "$target,0"
    $shortcut.Save()

    $version = (Get-Item $target).VersionInfo.ProductVersion
    Write-Host "설치 완료: $target"
    Write-Host "버전: $version"
    Write-Host "SHA-256 일치: True"

    if (-not $NoStart) {
        # 이 스크립트는 Program Files 쓰기 때문에 관리자 권한으로 실행된다.
        # Start-Process를 직접 쓰면 Memoria도 관리자 권한을 상속하여, 이후 일반 권한
        # 시작프로그램 인스턴스가 named pipe에 접근하지 못할 수 있다. Explorer COM
        # 서버를 통해 현재 로그인 사용자의 일반 권한으로 실행한다.
        $desktopShell = New-Object -ComObject Shell.Application
        $desktopShell.ShellExecute($target, "", $InstallDirectory, "open", 1)
        Start-Sleep -Seconds 3
        $running = Get-Process Memoria -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $target } |
            Select-Object -First 1 Id, ProcessName, Path
        if ($null -eq $running) { throw "설치는 완료됐지만 Memoria 프로세스가 시작되지 않았습니다." }
        $running | Format-Table -AutoSize
    }
}
finally {
    Pop-Location
}
