param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot 'ThreadLogViewer-win-x64'
$zipPath = Join-Path $artifactsRoot 'ThreadLogViewer-win-x64.zip'
try {
    Set-Location -LiteralPath $projectRoot
    if ((Test-Path -LiteralPath $artifactsRoot) -and ((Get-Item -LiteralPath $artifactsRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Artifacts folder must not be a link.' }
    # Resolve and verify the single generated folder before any recursive deletion.
    $resolvedPublish = [IO.Path]::GetFullPath($publishRoot)
    $expectedPublish = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\ThreadLogViewer-win-x64'))
    if ($resolvedPublish -ne $expectedPublish -or -not $resolvedPublish.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish path.' }
    if (Test-Path -LiteralPath $publishRoot) {
        if ((Get-Item -LiteralPath $publishRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish folder must not be a link.' }
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    & $Dotnet publish src/ThreadLogViewer.App/ThreadLogViewer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: exit $LASTEXITCODE" }
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'ThreadLogViewer.exe'))) { throw 'Published EXE was not created.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'ICSharpCode.AvalonEdit.dll'))) { throw 'AvalonEdit dependency is missing.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'samples') -Destination (Join-Path $publishRoot 'samples') -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination (Join-Path $publishRoot 'docs') -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishRoot
    Compress-Archive -LiteralPath $publishRoot -DestinationPath $zipPath -Force
    Write-Host "Folder: $publishRoot"
    Write-Host "ZIP: $zipPath"
    $hashStream = [IO.File]::OpenRead($zipPath)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $zipHash = [BitConverter]::ToString($sha.ComputeHash($hashStream)).Replace('-', '') }
    finally { $hashStream.Dispose(); $sha.Dispose() }
    Write-Host "SHA256: $zipHash"
} catch {
    Write-Error $_
    exit 1
}
