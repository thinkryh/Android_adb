@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
set "EXE=%ROOT%bin\Release\net10.0-windows\publish\Android投屏助手.exe"
if exist "%EXE%" (
    start "" "%EXE%"
    endlocal
    exit /b 0
)
set "EXE=%ROOT%bin\Release\net10.0-windows\win-x64\publish\Android投屏助手.exe"
if not exist "%EXE%" set "EXE=%ROOT%bin\Release\net10.0-windows\Android投屏助手.exe"
if not exist "%EXE%" (
    echo 未找到 v2 图形程序，请先执行构建和发布。
    pause
    exit /b 1
)
start "" "%EXE%"
endlocal
