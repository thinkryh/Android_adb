@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
set "V2_GLASS_V15=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v15\Android投屏助手.exe"
if exist "%V2_GLASS_V15%" (
    start "" "%V2_GLASS_V15%"
    exit /b 0
)
set "V2_GLASS_V14=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v14\Android投屏助手.exe"
if exist "%V2_GLASS_V14%" (
    start "" "%V2_GLASS_V14%"
    exit /b 0
)
set "V2_GLASS_V13=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v13\Android投屏助手.exe"
if exist "%V2_GLASS_V13%" (
    start "" "%V2_GLASS_V13%"
    exit /b 0
)
set "V2_GLASS_V12=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v12\Android投屏助手.exe"
if exist "%V2_GLASS_V12%" (
    start "" "%V2_GLASS_V12%"
    exit /b 0
)
set "V2_GLASS_V11=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v11\Android投屏助手.exe"
if exist "%V2_GLASS_V11%" (
    start "" "%V2_GLASS_V11%"
    exit /b 0
)
set "V2_GLASS_V10=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v10\Android投屏助手.exe"
if exist "%V2_GLASS_V10%" (
    start "" "%V2_GLASS_V10%"
    exit /b 0
)
set "V2_GLASS_V9=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v9\Android投屏助手.exe"
if exist "%V2_GLASS_V9%" (
    start "" "%V2_GLASS_V9%"
    exit /b 0
)
set "V2_GLASS_V8=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v8\Android投屏助手.exe"
if exist "%V2_GLASS_V8%" (
    start "" "%V2_GLASS_V8%"
    exit /b 0
)
set "V2_GLASS_V7=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v7\Android投屏助手.exe"
if exist "%V2_GLASS_V7%" (
    start "" "%V2_GLASS_V7%"
    exit /b 0
)
set "V2_GLASS_V3=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-v3\Android投屏助手.exe"
if exist "%V2_GLASS_V3%" (
    start "" "%V2_GLASS_V3%"
    exit /b 0
)
set "V2_GLASS_FIXED=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass-fixed\Android投屏助手.exe"
if exist "%V2_GLASS_FIXED%" (
    start "" "%V2_GLASS_FIXED%"
    exit /b 0
)
set "V2_GLASS=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\glass\Android投屏助手.exe"
if exist "%V2_GLASS%" (
    start "" "%V2_GLASS%"
    exit /b 0
)
set "V2_LATEST=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\latest\Android投屏助手.exe"
if exist "%V2_LATEST%" (
    start "" "%V2_LATEST%"
    exit /b 0
)
set "V2_CURRENT=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\current\Android投屏助手.exe"
if exist "%V2_CURRENT%" (
    start "" "%V2_CURRENT%"
    exit /b 0
)
set "V2_FX=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\publish\Android投屏助手.exe"
if exist "%V2_FX%" (
    start "" "%V2_FX%"
    exit /b 0
)
set "V2=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\win-x64\publish\Android投屏助手.exe"
if exist "%V2%" (
    start "" "%V2%"
    exit /b 0
)
set "V2_DEV=%ROOT%Android投屏助手_v2\bin\Release\net10.0-windows\Android投屏助手.exe"
if exist "%V2_DEV%" (
    start "" "%V2_DEV%"
    exit /b 0
)
for %%I in ("%~dp0程序组件\Android投屏助手.ps1") do set "MIRROR_HELPER=%%~sI"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%MIRROR_HELPER%"
exit /b %errorlevel%
