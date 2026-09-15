# Run only after the project user's approval. Downloads and extracts into .tools; no system setup.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsRoot = Join-Path $projectRoot '.tools'
$sdkRoot = Join-Path $toolsRoot 'dotnet'
try {
    if (Test-Path -LiteralPath $sdkRoot) { throw "SDK folder already exists; inspect it before installing: $sdkRoot" }
    if ((Test-Path -LiteralPath $toolsRoot) -and ((Get-Item -LiteralPath $toolsRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '.tools must not be a link.' }
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
    $releases = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $version = $releases.'latest-sdk'
    $sdk = $releases.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $version } | Select-Object -First 1
    $download = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $download -or ([Uri]$download.url).Host -notin @('builds.dotnet.microsoft.com','download.visualstudio.microsoft.com')) { throw 'No trusted SDK ZIP was found.' }
    $archive = Join-Path $toolsRoot "dotnet-sdk-$version-win-x64.zip"
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $download.url -OutFile $archive -UseBasicParsing
    $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
    if ($actualHash -ne $download.hash) { throw 'SDK SHA512 mismatch; extraction was not attempted.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $sdkRoot)
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
    & (Join-Path $sdkRoot 'dotnet.exe') --info
    if ($LASTEXITCODE -ne 0) { throw 'Extracted SDK could not run.' }
    Write-Host "Installed local SDK $version at $sdkRoot"
} catch {
    Write-Error $_
    exit 1
}
