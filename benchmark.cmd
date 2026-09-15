@echo off
setlocal
cd /d "%~dp0"
call scripts\use-dotnet.cmd
if errorlevel 1 exit /b 1
set "BENCHMARK_MIB=%~1"
if "%BENCHMARK_MIB%"=="" set "BENCHMARK_MIB=10"
"%DOTNET_EXE%" run --project tests\ThreadLogViewer.Tests -c Release -- --benchmark local-data %BENCHMARK_MIB%
set "TASK_EXIT=%ERRORLEVEL%"
if not "%TASK_EXIT%"=="0" echo ERROR: Benchmark failed with exit code %TASK_EXIT%.
exit /b %TASK_EXIT%
