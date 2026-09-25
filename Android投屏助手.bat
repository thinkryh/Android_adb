@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
set "APP=%ROOT%发布版\Android投屏助手.exe"
if exist "%APP%" (
    start "" "%APP%"
    exit /b 0
)
set "APP=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\Android投屏助手.exe"
if exist "%APP%" (
    start "" "%APP%"
    exit /b 0
)
for %%I in ("%ROOT%程序组件\Android投屏助手.ps1") do set "MIRROR_HELPER=%%~sI"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%MIRROR_HELPER%"
exit /b %errorlevel%
