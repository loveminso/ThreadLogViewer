param(
    [string]$ResultsDirectory = 'TestResults/release',
    [string]$Package = 'artifacts/ThreadLogViewer-win-x64.zip'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $projectRoot
[xml]$properties = Get-Content -LiteralPath 'Directory.Build.props' -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unsupported release version: $version" }

$trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
if ($trxFiles.Count -ne 1) { throw 'Expected exactly one completed test result.' }
[xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
$counter = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
if ($null -eq $counter) { throw 'Test result counters are missing.' }
$total = [int]$counter.GetAttribute('total')
$passed = [int]$counter.GetAttribute('passed')
$failed = [int]$counter.GetAttribute('failed')
if ($total -le 0 -or $passed -ne $total -or $failed -ne 0) {
    throw "All tests must pass before publishing: total=$total passed=$passed failed=$failed"
}

$publishRoot = Join-Path $projectRoot 'artifacts/ThreadLogViewer-win-x64'
$packagePath = [IO.Path]::GetFullPath((Join-Path $projectRoot $Package))
$required = @(
    'ThreadLogViewer.exe', 'ThreadLogViewer.dll', 'ThreadLogViewer.Core.dll',
    'ThreadLogViewer.runtimeconfig.json', 'ICSharpCode.AvalonEdit.dll',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
    'PresentationFramework.dll', 'WindowsBase.dll', 'README.md', 'THIRD-PARTY-NOTICES.md',
    'licenses/AvalonEdit-LICENSE.txt', 'licenses/dotnet-runtime-LICENSE.txt',
    'licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt', 'licenses/dotnet-wpf-LICENSE.txt',
    'licenses/dotnet-wpf-THIRD-PARTY-NOTICES.txt',
    'docs/SHORTCUTS.md', 'docs/FEATURE-TEST-GUIDE.md', 'docs/TESTING.md',
    'samples/demo-100-lines.log', 'samples/multiline-elapsed.log'
)
foreach ($relative in $required) {
    $file = Join-Path $publishRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Required package file is missing or empty: $relative"
    }
}
$expectedVersion = "$version.0"
foreach ($name in @('ThreadLogViewer.exe', 'ThreadLogViewer.dll')) {
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishRoot $name)).FileVersion
    if ($actual -ne $expectedVersion) { throw "Wrong $name version: $actual, expected $expectedVersion" }
}
$exe = [IO.File]::ReadAllBytes((Join-Path $publishRoot 'ThreadLogViewer.exe'))
$pe = [BitConverter]::ToInt32($exe, 0x3c)
if ([BitConverter]::ToUInt16($exe, $pe + 4) -ne 0x8664) { throw 'The executable must be Windows x64.' }
$runtime = Get-Content -LiteralPath (Join-Path $publishRoot 'ThreadLogViewer.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $runtime.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
    throw 'The package must include its runtime, without a separately installed .NET dependency.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    $files = @(Get-ChildItem -LiteralPath $publishRoot -File -Recurse)
    if ($entries.Count -ne $files.Count) { throw 'ZIP file count does not match the published folder.' }
    $rootName = [IO.Path]::GetFileName($publishRoot)
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($publishRoot, $file.FullName).Replace('\', '/')
        $entry = $archive.GetEntry("$rootName/$relative")
        if ($null -eq $entry -or $entry.Length -ne $file.Length) { throw "Invalid ZIP entry: $relative" }
        $stream = $entry.Open()
        try {
            $zipHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
        } finally { $stream.Dispose() }
        $fileHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($zipHash -ne $fileHash) { throw "ZIP content does not match: $relative" }
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$packagePath.sha256"
"$hash  $([IO.Path]::GetFileName($packagePath))" | Set-Content -LiteralPath $checksumPath -Encoding utf8NoBOM
$report = [ordered]@{
    version = $version
    sourceCommit = $env:GITHUB_SHA
    tests = @{ total = $total; passed = $passed; failed = $failed }
    package = [IO.Path]::GetFileName($packagePath)
    packageBytes = (Get-Item -LiteralPath $packagePath).Length
    packageFiles = $files.Count
    sha256 = $hash
    scope = 'Windows Release build, automated tests and package integrity. No manual GUI, monitor DPI or new performance measurement.'
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'artifacts/release-verification.json' -Encoding utf8NoBOM
Write-Host "Verified v$version, $passed/$total tests, $($files.Count) package files, SHA256 $hash"
if ($env:GITHUB_OUTPUT) {
    "version=$version" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    "passed=$passed" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    "total=$total" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    "hash=$hash" | Add-Content -LiteralPath $env:GITHUB_OUTPUT
}
