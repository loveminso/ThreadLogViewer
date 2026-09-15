@echo off
setlocal
cd /d "%~dp0"
call scripts\use-dotnet.cmd
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -Dotnet "%DOTNET_EXE%"
set "TASK_EXIT=%ERRORLEVEL%"
if not "%TASK_EXIT%"=="0" echo ERROR: Publish failed with exit code %TASK_EXIT%. Read the error above.
exit /b %TASK_EXIT%
