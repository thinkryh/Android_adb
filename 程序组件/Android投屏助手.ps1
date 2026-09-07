param([string]$Mode)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$HelperPath = if ($PSCommandPath) { $PSCommandPath } else { $env:MIRROR_HELPER }
$BundleRoot = Split-Path -Parent (Split-Path -Parent $HelperPath)
$ScrcpyRoot = Join-Path $BundleRoot 'scrcpy'
$ScrcpyExe = Join-Path $ScrcpyRoot 'scrcpy.exe'
$AdbPath = Join-Path $ScrcpyRoot 'adb.exe'
$LastWifiPath = Join-Path $BundleRoot '程序组件\上次无线地址.txt'
$AndroidConfig = Join-Path $BundleRoot '程序组件\.android'
New-Item -ItemType Directory -Path $AndroidConfig -Force | Out-Null
$env:ANDROID_USER_HOME = $AndroidConfig
$env:HOME = $BundleRoot
$env:USERPROFILE = $BundleRoot
$env:HOMEDRIVE = [IO.Path]::GetPathRoot($BundleRoot).TrimEnd('\')
$env:HOMEPATH = $BundleRoot.Substring($env:HOMEDRIVE.Length)
$script:AdbSelector = ''
$script:DeviceSerial = ''
function Say([string]$Text, [ConsoleColor]$Color = 'Gray') {
    Write-Host ('  ' + $Text) -ForegroundColor $Color
}
function Launch([string]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $ScrcpyExe
    $info.WorkingDirectory = $ScrcpyRoot
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $env:ADB = $AdbPath
    return [Diagnostics.Process]::Start($info)
}
function RunAdb([string]$Arguments, [string]$SuccessPattern = '') {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $AdbPath
    $selectedArguments = if ($script:AdbSelector) { "$script:AdbSelector $Arguments" } else { $Arguments }
    $info.Arguments = $selectedArguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(15000)) { $process.Kill(); throw '手机连接操作超时。' }
    $output = ($process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()).Trim()
    if ($process.ExitCode -ne 0) {
        if ($output -match 'cannot connect|拒绝|failed|unauthorized') { throw "无线连接失败：$output" }
        throw "无线调试操作失败：$output"
    }
    if ($SuccessPattern -and $output -notmatch $SuccessPattern) { throw "手机没有返回成功结果：$output" }
    return $output
}
try {
    if ($Mode -eq 'selftest') {
        $probe = Launch '--version'
        if (-not $probe.WaitForExit(10000)) { throw '检查超时。' }
        if ($probe.ExitCode -ne 0) { throw '检查失败。' }
        Say '检查通过：程序可通过无控制台方式启动。' Green
        exit 0
    }
    try { $Host.UI.RawUI.WindowTitle = 'Android投屏助手' } catch {}
    Write-Host ''
    Say 'Android投屏助手' Cyan
    Say '稳定清晰模式' DarkGray
    Say '----------------------------------------' DarkGray
    Write-Host ''
    Say '中文输入' Cyan
    Say '电脑切英文，手机切中文拼音。'
    Say '点击手机输入框，输入并选择候选词。'
    Write-Host ''
    Say '常用快捷键' Cyan
    Say '先点击投屏画面；以下均使用左侧 Alt 键。'
    Say 'Alt + F           全屏或退出全屏'
    Say 'Alt + H           返回手机桌面'
    Say 'Alt + B           返回上一页'
    Say 'Alt + S           查看最近应用'
    Say 'Alt + V           粘贴电脑复制的文字'
    Say 'Alt + K           打开手机外接键盘设置'
    Say 'Alt + G           按原始像素大小显示'
    Say 'Alt + Shift + R   重置画面采集，尝试恢复清晰'
    Write-Host ''
    Say '----------------------------------------' DarkGray
    if ($Mode -eq 'preview') {
        Say '界面预览完成，未启动手机投屏。' Green
        exit 0
    }
    $adbPath = $AdbPath
    if (-not (Test-Path -LiteralPath $ScrcpyExe)) { throw '找不到投屏程序，请检查安装目录。' }
    if (-not (Test-Path -LiteralPath $adbPath)) { throw '找不到手机连接工具，请检查安装目录。' }
    $deviceList = RunAdb 'devices'
    $deviceLines = $deviceList -split "`r?`n" | Where-Object { $_ -match '^\S+\s+device\s*$' }
    $usbDevice = $deviceLines | Where-Object { ($_ -split '\s+')[0] -notmatch '^\d{1,3}(\.\d{1,3}){3}:\d+$' } | Select-Object -First 1
    $wifiDevice = $deviceLines | Where-Object { ($_ -split '\s+')[0] -match '^\d{1,3}(\.\d{1,3}){3}:\d+$' } | Select-Object -First 1
    $wired = $null -ne $usbDevice
    if ($wired) {
        $script:DeviceSerial = ($usbDevice -split '\s+')[0]
        $script:AdbSelector = "-s $script:DeviceSerial"
        Say '检测到 USB 手机，将使用有线设备。' Green
    } elseif ($wifiDevice) {
        $script:DeviceSerial = ($wifiDevice -split '\s+')[0]
        $script:AdbSelector = "-s $script:DeviceSerial"
        Say "检测到已连接的无线手机，将使用：$script:DeviceSerial" Green
    }
    if (-not $wired -and -not $wifiDevice) {
        Say '未检测到数据线设备，准备尝试无线连接。' Yellow
        Say '手机和电脑需要连接同一个无线网络。' Cyan
        Say '安卓11：请打开 设置 → 更多设置 → 开发者选项 → 无线调试。'
        Say '配对端口每次可能变化；连接端口来自无线调试主页面，通常比较稳定。'
        $lastWifiAddress = ''
        if (Test-Path -LiteralPath $LastWifiPath) { $lastWifiAddress = ([IO.File]::ReadAllText($LastWifiPath,[Text.Encoding]::UTF8)).Trim() }
        $wifiAddress = $lastWifiAddress
        $connected = $false
        if ($wifiAddress -and $wifiAddress -match '^\d{1,3}(\.\d{1,3}){3}:\d{1,5}$') {
            Say "正在尝试上次连接地址：$wifiAddress" Cyan
            try { [void](RunAdb "connect $wifiAddress" 'connected to|already connected|已连接'); $connected = $true } catch { }
        }
        if (-not $connected) {
            $pairAddress = Read-Host '请输入配对地址（例如 192.168.1.10:37001）'
            if ([string]::IsNullOrWhiteSpace($pairAddress)) { throw '未输入配对地址。' }
            if ($pairAddress -notmatch '^\d{1,3}(\.\d{1,3}){3}:\d{1,5}$') { throw '配对地址格式不正确，必须是 IP:端口。' }
            $pairCode = Read-Host '请输入手机显示的六位配对码'
            if ([string]::IsNullOrWhiteSpace($pairCode)) { throw '未输入配对码。' }
            [void](RunAdb "pair $pairAddress $pairCode" 'Successfully paired|配对成功')
            $wifiAddress = Read-Host '请输入连接地址（例如 192.168.1.10:40001）'
            if ([string]::IsNullOrWhiteSpace($wifiAddress)) { throw '未输入连接地址。' }
            if ($wifiAddress -notmatch '^\d{1,3}(\.\d{1,3}){3}:\d{1,5}$') { throw '连接地址格式不正确，必须是 IP:端口。' }
            [void](RunAdb "connect $wifiAddress" 'connected to|already connected|已连接')
        }
        [IO.File]::WriteAllText($LastWifiPath,$wifiAddress,[Text.UTF8Encoding]::new($false))
        $script:DeviceSerial = $wifiAddress
        $script:AdbSelector = "-s $script:DeviceSerial"
        Say '无线调试连接成功。' Green
    } elseif ($wired) {
        Say '数据线连接成功，将优先使用有线投屏。' Green
    } else {
        Say '无线手机已连接，将直接使用无线投屏。' Green
    }
    Say '正在打开投屏窗口……' Cyan
    $mirrorSelector = if ($script:DeviceSerial) { "--serial=$script:DeviceSerial" } else { '' }
    $wireless = $script:DeviceSerial -match '^\d{1,3}(\.\d{1,3}){3}:\d+$'
    if ($wireless) {
        $videoArgs = '--max-size=1600 --video-bit-rate=12M --max-fps=30 --video-buffer=30'
        Say '无线模式：默认使用均衡画质。' Cyan
    } else {
        $videoArgs = '--max-size=1920 --video-bit-rate=16M --max-fps=30'
    }
    $mirror = Launch "$mirrorSelector --window-title=Android投屏助手 --stay-awake $videoArgs --video-codec=h264 --keep-active --audio-bit-rate=128K --keyboard=uhid --no-mouse-hover --verbosity=error"
    if ($mirror.WaitForExit(2000)) { throw '投屏已退出，请检查手机连接和调试授权后重试。' }
    Say '投屏已启动，可保留本窗口查阅快捷键。' Green
    Say '点本窗口右上角关闭按钮，投屏仍会继续。'
    while ($true) { Start-Sleep -Seconds 1 }
} catch {
    Say '启动未完成' Yellow
    $reason = $_.Exception.Message
    if ($reason -notmatch '[\u4e00-\u9fff]') { $reason = '程序启动失败，请检查软件安装和系统权限。' }
    Say $reason Yellow
    if ($Mode -ne 'preview' -and $Mode -ne 'selftest') {
        Say '按回车键关闭此窗口。'
        [void][Console]::ReadLine()
    }
    exit 1
}
