# 第三方组件与许可

本项目自行编写的 WPF、批处理、PowerShell 源码和文档按根目录 [MIT 许可证](LICENSE)授权。以下随附组件各自遵循其上游许可证；项目 MIT 许可证不覆盖这些组件。

| 随附文件 | 上游项目与版本 | 许可证与来源 |
| --- | --- | --- |
| `scrcpy/scrcpy.exe`、`scrcpy/scrcpy-server`、`scrcpy/scrcpy-noconsole.vbs` | [scrcpy 4.1](https://github.com/Genymobile/scrcpy/releases/tag/v4.1) | Apache-2.0；随附 [原文](scrcpy/LICENSE.txt) |
| `scrcpy/adb.exe`、`AdbWinApi.dll`、`AdbWinUsbApi.dll` | [Android SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools) | 来自 scrcpy 官方 Windows 发布包；应核对 [Android SDK 许可](https://developer.android.com/studio/terms)和其原始分发要求 |
| `scrcpy/SDL3.dll` | [SDL 3.4.12](https://github.com/libsdl-org/SDL/tree/release-3.4.12) | [zlib 许可](third_party_licenses/SDL-zlib.txt) |
| `scrcpy/avcodec-62.dll`、`avformat-62.dll`、`avutil-60.dll`、`swresample-6.dll` | [FFmpeg 8.1.2](https://ffmpeg.org/) | 通常为 LGPL-2.1-or-later；已附 [LGPL 2.1 文本](third_party_licenses/FFmpeg-LGPL-2.1.txt)，但[实际构建配置及 GPL／非自由选项必须逐包确认](https://ffmpeg.org/legal.html) |
| `scrcpy/libusb-1.0.dll` | [libusb 1.0.30](https://github.com/libusb/libusb/releases/tag/v1.0.30) | [LGPL-2.1-or-later](third_party_licenses/libusb-LGPL-2.1.txt) |
| 构建输出中的 `QRCoder.dll` | [QRCoder 1.6.0](https://www.nuget.org/packages/QRCoder/1.6.0) | [MIT](third_party_licenses/QRCoder-MIT.txt)；本地 NuGet 包声明为 MIT |

来源核对：本地 `scrcpy/` 的 13 个文件分别与 scrcpy 官方 `scrcpy-win64-v4.1.zip` 中对应文件的 SHA-256 一致。官方压缩包 SHA-256 为 `5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db`；未复制官方压缩包中的 `disconnected.png`、`scrcpy.png` 和 `open_a_terminal_here.bat`。此核对只证明文件与该官方发布包一致，不等同于完成所有依赖的法律合规审查。

## 再分发前仍需核对

1. 确认上述 FFmpeg DLL 的精确构建选项与对应源码，按 [FFmpeg 发布核对清单](https://ffmpeg.org/legal.html)提供许可文本、署名及相应源码获取方式。FFmpeg 是否启用了 GPL 或非自由组件尚未核实，不能仅凭 DLL 名称认定可再分发条款。
2. 随可执行发布包保留 `scrcpy/LICENSE.txt`、`third_party_licenses/` 和本声明；核对 Android SDK Platform-Tools 的分发条件。FFmpeg 若启用了 GPL／非自由选项，还需增加或更换适用文本并满足对应要求。
3. 图标由项目用户提供；上架或对外再分发前由权利人确认图标素材的使用与授权范围。

本清单记录已查明的文件来源和待核对项，不能作为第三方法律认证证明。

2026-09-26 核查：GitHub API 将 `thinkryh/Android_adb` 标记为公开仓库，并识别根目录许可证为 `MIT`。这只确认仓库状态和项目自身的许可证识别，不代表上述第三方再分发要求已全部满足。
