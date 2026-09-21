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

function Stop-Memoria {
    $processes = @(Get-Process Memoria -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }

    $processes | Stop-Process -Force
    foreach ($process in $processes) {
        try { $process.WaitForExit(10000) | Out-Null } catch { }
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
        Start-Process $target
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
