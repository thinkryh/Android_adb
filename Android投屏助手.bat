@echo off
setlocal
chcp 65001 >nul
for %%I in ("%~dp0程序组件\Android投屏助手.ps1") do set "MIRROR_HELPER=%%~sI"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%MIRROR_HELPER%"
exit /b %errorlevel%
