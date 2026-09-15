@echo off
setlocal
cd /d "%~dp0"
call scripts\use-dotnet.cmd
if errorlevel 1 exit /b 1
"%DOTNET_EXE%" build ThreadLogViewer.slnx -c Release
set "TASK_EXIT=%ERRORLEVEL%"
if not "%TASK_EXIT%"=="0" echo ERROR: Build failed with exit code %TASK_EXIT%.
exit /b %TASK_EXIT%
