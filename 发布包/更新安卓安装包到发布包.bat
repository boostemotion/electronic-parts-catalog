@echo off
setlocal

set "ROOT=%~dp0.."
node "%ROOT%\scripts\android-publish.mjs"
exit /b %ERRORLEVEL%
