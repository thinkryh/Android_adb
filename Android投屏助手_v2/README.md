# Android 投屏助手 v2（WPF 预览版）

这是 v1 的 WPF 图形界面升级版本，保留仓库内的 scrcpy 和 adb 作为投屏核心。

当前已实现：

- 真实读取 adb devices -l 并展示多台设备。
- USB、IP 无线连接、配对码配对。
- adb mdns services 无线服务发现。
- Android 11 无线调试扫码配对二维码生成和配对服务等待。
- 每台设备独立启动 scrcpy。
- 无线默认均衡画质：1600 像素、12 Mbps、30 帧、30ms 缓冲。
- 玻璃卡片风格的 WPF 主界面。
- 投屏悬浮工具栏：主页、返回、最近任务、截图、全屏、息屏、录屏。
- 工具栏支持拖动手柄，可吸附到投屏窗口左侧或右侧，并在投屏窗口移动时跟随。
- 主界面和工具栏使用清晰的硬件渲染路径，减少透明窗口在高 DPI 下的模糊。
- 录屏使用 Android 自带 screenrecord，结束后保存到“视频\\Android投屏助手”。
- 配置和无线历史地址写入当前用户的本地应用数据目录。
- 根目录的启动批处理会优先打开 v2 图形程序，找不到发布程序时回退到 v1。

## 构建

需要 .NET 10 SDK 和 Windows 桌面组件。执行 dotnet restore、dotnet build -c Release，然后执行 dotnet publish -c Release --self-contained false。

项目会从上级 scrcpy 文件夹复制 scrcpy.exe、adb.exe 和相关运行库。

## 当前限制

- Liquid Glass 是 Windows WPF 的视觉实现，提供卡片、圆角和层次感，并非苹果系统材质的逐像素复刻。
- 扫码流程依赖 Android 11+ 无线调试和 ADB mDNS 支持。
- 工具栏跟随和吸附需要在真实设备、多显示器和不同 DPI 下进一步验证。
- 录屏依赖 Android 设备提供 screenrecord 和 pidof 命令；部分厂商系统可能限制录屏或后台 shell 进程。
- 当前版本先提供投屏核心快捷操作，文件管理、APK 管理、自动化和 AI 功能不在 v2 首版范围内。
