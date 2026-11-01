@echo off
setlocal

set "LAUNCHER_DIR=%~dp0"
set "START_HELPER=%LAUNCHER_DIR%Start-WindowsProgram.ps1"
set "RUNTIME_IDENTIFIER=win-x64"

rem Published package mode: launcher sits beside the executable.
set "EXE_PATH=%LAUNCHER_DIR%ComponentCatalogWinUI.exe"
if exist "%EXE_PATH%" (
  set "STARTUP_LOG=%LAUNCHER_DIR%ComponentCatalogData\Logs\startup.log"
  goto launch_published
)

rem Repository mode: the helper holds the mutex across stop, build, launch, and verification.
for %%I in ("%LAUNCHER_DIR%..") do set "ROOT=%%~fI"
set "PROJECT=%ROOT%\ComponentCatalogWinUI\ComponentCatalogWinUI.csproj"
set "LOCAL_DOTNET=%ROOT%\.dotnet-sdk\dotnet.exe"
set "EXE_PATH=%ROOT%\ComponentCatalogWinUI\bin\Release\net10.0-windows10.0.26100.0\%RUNTIME_IDENTIFIER%\ComponentCatalogWinUI.exe"
set "STARTUP_LOG=%ROOT%\ComponentCatalogData\Logs\startup.log"

if not exist "%PROJECT%" (
  echo Neither a published executable nor the WinUI project was found.
  echo Launcher: %~f0
  echo Expected published EXE: %LAUNCHER_DIR%ComponentCatalogWinUI.exe
  echo Expected project: %PROJECT%
  pause
  exit /b 1
)

if exist "%LOCAL_DOTNET%" (
  set "DOTNET=%LOCAL_DOTNET%"
) else (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo .NET SDK was not found.
    echo Checked: %LOCAL_DOTNET%
    pause
    exit /b 1
  )
  set "DOTNET=dotnet"
)

goto launch_repository

:launch_published
if not exist "%START_HELPER%" (
  echo Windows launch helper was not found:
  echo %START_HELPER%
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%START_HELPER%" -ExecutablePath "%EXE_PATH%" -StartupLogPath "%STARTUP_LOG%" -MutexTimeoutSeconds 60
goto check_launch_result

:launch_repository
if not exist "%START_HELPER%" (
  echo Windows launch helper was not found:
  echo %START_HELPER%
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%START_HELPER%" -ExecutablePath "%EXE_PATH%" -StartupLogPath "%STARTUP_LOG%" -RepositoryProject "%PROJECT%" -DotnetPath "%DOTNET%" -MutexTimeoutSeconds 60

:check_launch_result
if errorlevel 1 (
  echo.
  echo Windows app launch failed.
  pause
  exit /b 1
)

exit /b 0
