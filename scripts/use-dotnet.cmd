@echo off
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_HOME=%~dp0..\.dotnet-home"
set "NUGET_PACKAGES=%~dp0..\.nuget\packages"
set "DOTNET_EXE=dotnet"
if exist "%~dp0..\.tools\dotnet\dotnet.exe" (
  set "DOTNET_EXE=%~dp0..\.tools\dotnet\dotnet.exe"
  set "DOTNET_ROOT=%~dp0..\.tools\dotnet"
)
"%DOTNET_EXE%" --version
if not "%ERRORLEVEL%"=="0" (
  echo ERROR: .NET 10 SDK is required. See README.md.
  exit /b 1
)
exit /b 0
