@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

rem ============================================================
rem  一键解除启动提示（需要管理员权限，只需运行一次）
rem  1) 关闭对本地文件的“打开文件-安全警告”提示
rem  2) 关闭对未签名本地程序的 SmartScreen“已保护你的电脑”
rem  3) 将本仓库加入 Windows Defender 排除目录
rem  4) 清除所有文件的网络来源标记（Mark of the Web）
rem ============================================================

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo 正在请求管理员权限，请在弹出的 UAC 窗口点击“是”...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo [1/4] 将本地脚本/程序设为低风险类型（不再弹“打开文件-安全警告”）...
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Associations" /v LowRiskFileTypes /t REG_SZ /d ".bat;.cmd;.exe;.lnk;.ps1" /f >nul
if %errorlevel% equ 0 (echo     完成) else (echo     失败：错误代码 %errorlevel%)

echo [2/4] 关闭 SmartScreen 对本地未签名程序的检测（不再弹“已保护你的电脑”）...
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost" /v EnableWebContentEvaluation /t REG_DWORD /d 0 /f >nul
reg add "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost" /v PreventOverride /t REG_DWORD /d 0 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer" /v SmartScreenEnabled /t REG_SZ /d Off /f >nul
if %errorlevel% equ 0 (echo     完成) else (echo     失败：错误代码 %errorlevel%)

echo [3/4] 将本仓库加入 Windows Defender 排除目录...
set "REPO=%~dp0.."
powershell -NoProfile -Command "try { Add-MpPreference -ExclusionPath (Resolve-Path '%REPO%').Path -ErrorAction Stop; Write-Host '    完成' } catch { Write-Host ('    跳过：' + $_.Exception.Message) }"

echo [4/4] 清除仓库内所有文件的网络来源标记...
powershell -NoProfile -Command "Get-ChildItem (Resolve-Path '%REPO%').Path -Recurse -File -ErrorAction SilentlyContinue | Unblock-File; Write-Host '    完成'"

echo.
echo ============================================================
echo  全部完成！以后直接双击桌面上的“电子元件入库台”即可，
echo  不再出现任何安全提示。
echo ============================================================
pause
