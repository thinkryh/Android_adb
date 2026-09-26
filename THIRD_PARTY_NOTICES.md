# 第三方组件与许可

本项目自行编写的 WPF、批处理、PowerShell 源码和文档按根目录 [MIT 许可证](LICENSE)授权。以下随附组件各自遵循其上游许可证；项目 MIT 许可证不覆盖这些组件。

| 随附文件 | 上游项目与版本 | 许可证与来源 |
| --- | --- | --- |
| `scrcpy/scrcpy.exe`、`scrcpy/scrcpy-server`、`scrcpy/scrcpy-noconsole.vbs` | [scrcpy 4.1](https://github.com/Genymobile/scrcpy/releases/tag/v4.1) | Apache-2.0；随附 [原文](scrcpy/LICENSE.txt) |
| `scrcpy/adb.exe`、`AdbWinApi.dll`、`AdbWinUsbApi.dll` | [Android SDK Platform-Tools 37.0.0](https://developer.android.com/tools/releases/platform-tools) | 三个文件与 Google 官方 Windows 包逐个 SHA-256 一致；随附该包的完整 [NOTICE](third_party_licenses/Android-Platform-Tools-37.0.0-NOTICE.txt)，分发条件见下文 |
| `scrcpy/SDL3.dll` | [SDL 3.4.12](https://github.com/libsdl-org/SDL/tree/release-3.4.12) | [zlib 许可](third_party_licenses/SDL-zlib.txt) |
| `scrcpy/avcodec-62.dll`、`avformat-62.dll`、`avutil-60.dll`、`swresample-6.dll` | [FFmpeg 8.1.2](https://ffmpeg.org/) | LGPL-2.1-or-later；随附 [LGPL 2.1 文本](third_party_licenses/FFmpeg-LGPL-2.1.txt)与从 DLL 提取的[实际构建配置](third_party_licenses/FFmpeg-8.1.2-build-config.txt)，未发现 `--enable-gpl` 或 `--enable-nonfree` |
| 静态链接到上述 FFmpeg DLL 的 dav1d | [dav1d 1.5.3](https://github.com/videolan/dav1d/tree/1.5.3) | [BSD-2-Clause](third_party_licenses/dav1d-BSD-2-Clause.txt)；另附 [Alliance for Open Media Patent License 1.0](third_party_licenses/dav1d-AOM-Patent-License.txt) |
| 静态链接到上述 FFmpeg DLL 的 zlib | [zlib](https://zlib.net/)；确切构建版本未查明 | [zlib 许可文本](third_party_licenses/zlib-license.txt)；FFmpeg 构建脚本明确使用静态链接避免 zlib DLL 依赖 |
| `scrcpy/libusb-1.0.dll` | [libusb 1.0.30](https://github.com/libusb/libusb/releases/tag/v1.0.30) | [LGPL-2.1-or-later](third_party_licenses/libusb-LGPL-2.1.txt) |
| 构建输出中的 `QRCoder.dll` | [QRCoder 1.6.0](https://www.nuget.org/packages/QRCoder/1.6.0) | [MIT](third_party_licenses/QRCoder-MIT.txt)；本地 NuGet 包声明为 MIT |

来源核对：本地 `scrcpy/` 的 13 个文件分别与 scrcpy 官方 `scrcpy-win64-v4.1.zip` 中对应文件的 SHA-256 一致。官方压缩包 SHA-256 为 `5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db`；未复制官方压缩包中的 `disconnected.png`、`scrcpy.png` 和 `open_a_terminal_here.bat`。另独立核对了三个 ADB 文件与 Google 官方 `platform-tools_r37.0.0-win.zip` 完全一致，官方包 SHA-256 为 `4fe305812db074cea32903a489d061eb4454cbc90a49e8fea677f4b7af764918`。

随包 `avutil-60.dll` 中的 FFmpeg 配置串使用 `--enable-shared`，没有 `--enable-gpl`／`--enable-nonfree`，并含 `--enable-libdav1d` 和 `--enable-zlib`。scrcpy v4.1 [Windows 构建脚本](https://github.com/Genymobile/scrcpy/blob/v4.1/release/build_windows.sh)与依赖脚本表明 dav1d 1.5.3 静态构建、FFmpeg 8.1.2 作为 DLL 发布。与二进制对应的 FFmpeg、libusb、dav1d 上游源码包及校验值见[源码附件清单](third_party_licenses/SOURCE_ARCHIVES.md)。

## 再分发前仍需核对

1. 已补齐 FFmpeg 的实际配置、LGPL 文本和对应上游源码，但 [FFmpeg 发布核对清单](https://ffmpeg.org/legal.html)中的程序内署名等条目仍需逐项确认；静态链接的 zlib 确切版本尚未查明。
2. ADB 源码在 AOSP 中按 Apache-2.0 开源，且已随包提供官方 Platform-Tools NOTICE；但 Google [Android SDK 条款](https://developer.android.com/studio/terms)对 SDK 组件再分发设有限制及开源例外。此处仍需确认官方预编译 ADB 与两份 Windows DLL 是否完全落入该例外，不能仅凭 AOSP 源码许可认定整包已获再分发许可。
3. 图标由项目用户提供；其原创性、第三方素材与商标授权范围无法从文件本身核实。

本清单记录技术来源与许可材料，不是第三方法律认证。分发时应保留 `scrcpy/LICENSE.txt`、`third_party_licenses/` 和本声明；`v2.0.0-preview.1` 的应用 ZIP 缺少部分许可文本，应改用 `v2.0.0-preview.2` 或后续修正版。

2026-09-26 核查：GitHub API 将 `thinkryh/Android_adb` 标记为公开仓库，并识别根目录许可证为 `MIT`。这只确认仓库状态和项目自身的许可证识别，不代表上述第三方再分发要求已全部满足。
