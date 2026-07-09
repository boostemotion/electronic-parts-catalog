@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT_DIR=%ROOT%..\Android"
set "APK_SOURCE=%PROJECT_DIR%\android\app\build\outputs\apk\debug\app-debug.apk"
set "APK_TARGET_DIR=%ROOT%Android"
set "APK_TARGET=%APK_TARGET_DIR%\电子元件入库台-Android.apk"

if not exist "%PROJECT_DIR%\package.json" (
  echo Android project not found:
  echo %PROJECT_DIR%
  exit /b 1
)

if not exist "%APK_TARGET_DIR%" mkdir "%APK_TARGET_DIR%"

pushd "%PROJECT_DIR%" || exit /b 1

call npm run build
if errorlevel 1 goto :fail

call npm run android:sync
if errorlevel 1 goto :fail

call .\android\gradlew.bat -p .\android assembleDebug --no-daemon
if errorlevel 1 goto :fail

popd

copy /y "%APK_SOURCE%" "%APK_TARGET%" >nul
if errorlevel 1 (
  echo Failed to copy APK to:
  echo %APK_TARGET%
  exit /b 1
)

echo.
echo APK ready:
echo %APK_TARGET%
exit /b 0

:fail
set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
