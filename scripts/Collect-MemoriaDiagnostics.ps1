[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath("Desktop")) ("Memoria-Diagnostics-{0}.zip" -f (Get-Date -Format "yyyyMMdd-HHmmss")))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dataDirectory = Join-Path $env:LOCALAPPDATA "Memoria"
$installedExe = Join-Path ${env:ProgramFiles(x86)} "Memoria\Memoria.exe"
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ("Memoria-Diagnostics-{0}" -f [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Force -Path $workDirectory | Out-Null

    foreach ($name in @("memoria.log", "memoria.log.old", "memoria-performance.log", "memoria-performance.log.old")) {
        $source = Join-Path $dataDirectory $name
        if (Test-Path $source -PathType Leaf) {
            Copy-Item $source (Join-Path $workDirectory $name) -Force
        }
    }

    $processes = @(Get-Process Memoria -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, Path, StartTime, CPU, WorkingSet64, PrivateMemorySize64, HandleCount, @{ Name = "ThreadCount"; Expression = { $_.Threads.Count } })
    $installed = if (Test-Path $installedExe -PathType Leaf) {
        $file = Get-Item $installedExe
        [PSCustomObject]@{
            Path = $file.FullName
            ProductVersion = $file.VersionInfo.ProductVersion
            FileVersion = $file.VersionInfo.FileVersion
            Length = $file.Length
            SHA256 = (Get-FileHash $installedExe -Algorithm SHA256).Hash
        }
    } else { $null }

    [PSCustomObject]@{
        CollectedAt = (Get-Date).ToString("O")
        Windows = [Environment]::OSVersion.VersionString
        DotNetRuntimes = (& dotnet --list-runtimes 2>&1 | Out-String).Trim()
        ProcessorCount = [Environment]::ProcessorCount
        TotalMemoryBytes = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
        Installed = $installed
        Processes = $processes
        DatabaseBytes = if (Test-Path (Join-Path $dataDirectory "memoria.db")) { (Get-Item (Join-Path $dataDirectory "memoria.db")).Length } else { 0 }
        WalBytes = if (Test-Path (Join-Path $dataDirectory "memoria.db-wal")) { (Get-Item (Join-Path $dataDirectory "memoria.db-wal")).Length } else { 0 }
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $workDirectory "system-summary.json") -Encoding UTF8

    if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }
    Compress-Archive -Path (Join-Path $workDirectory "*") -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "진단 파일 생성 완료: $OutputPath"
    Write-Host "메모 본문과 데이터베이스 원본은 포함되지 않았습니다."
}
finally {
    if (Test-Path $workDirectory) { Remove-Item $workDirectory -Recurse -Force -ErrorAction SilentlyContinue }
}
