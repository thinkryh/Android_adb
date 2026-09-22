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
- 投屏悬浮工具栏：主页、返回、最近任务、音量、截图、置顶。
- 配置和无线历史地址写入当前用户的本地应用数据目录。
- 根目录的启动批处理会优先打开 v2 图形程序，找不到发布程序时回退到 v1。

## 构建

需要 .NET 10 SDK 和 Windows 桌面组件。执行 dotnet restore、dotnet build -c Release，然后执行 dotnet publish -c Release --self-contained false。

项目会从上级 scrcpy 文件夹复制 scrcpy.exe、adb.exe 和相关运行库。

## 当前限制

- Liquid Glass 是 Windows WPF 的视觉实现，提供半透明卡片和圆角层次，并非苹果系统材质的逐像素复刻。
- 扫码流程依赖 Android 11+ 无线调试和 ADB mDNS 支持。
- 悬浮工具栏的窗口跟随需要在真实设备、多显示器和不同 DPI 下进一步验证。
- 录屏入口尚未加入；需要先验证当前 scrcpy 版本的独立录制会话结束方式，确保 MP4 收尾完整。
- 当前版本先提供投屏核心快捷操作，文件管理、APK 管理、自动化和 AI 功能不在 v2 首版范围内。
