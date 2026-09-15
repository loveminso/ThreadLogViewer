@echo off
setlocal
cd /d "%~dp0"
call scripts\use-dotnet.cmd
if errorlevel 1 exit /b 1
"%DOTNET_EXE%" test ThreadLogViewer.slnx -c Release --logger "trx;LogFileName=tests.trx" --results-directory TestResults
set "TASK_EXIT=%ERRORLEVEL%"
if not "%TASK_EXIT%"=="0" echo ERROR: Automated tests failed with exit code %TASK_EXIT%.
exit /b %TASK_EXIT%
