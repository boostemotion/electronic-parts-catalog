@echo off
setlocal

set "ROOT=%~dp0"
set "EXE_DIR=%ROOT%..\ComponentCatalogWinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"
set "EXE_PATH=%EXE_DIR%\ComponentCatalogWinUI.exe"

if not exist "%EXE_PATH%" (
  echo Windows executable not found:
  echo %EXE_PATH%
  echo.
  echo Please build/publish the WinUI project first.
  exit /b 1
)

start "" /d "%EXE_DIR%" "%EXE_PATH%"
exit /b 0
