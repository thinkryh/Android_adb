# 与 Windows x64 发布包对应的上游源码

这些源码压缩包作为同一 GitHub Release 的独立附件提供，不计入应用 ZIP。版本及 SHA-256 取自 scrcpy v4.1 的构建脚本；本项目下载后重新计算并核对过哈希。

| 组件 | 文件 | SHA-256 |
| --- | --- | --- |
| FFmpeg 8.1.2 | [ffmpeg-8.1.2.tar.xz](https://github.com/thinkryh/Android_adb/releases/download/v2.0.0-preview.2/ffmpeg-8.1.2.tar.xz) | `464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c` |
| libusb 1.0.30 | [libusb-1.0.30.tar.gz](https://github.com/thinkryh/Android_adb/releases/download/v2.0.0-preview.2/libusb-1.0.30.tar.gz) | `2ae28adb0bb9558c86135c4e1c11b320b0805461e207a64a6e520a114094bf07` |
| dav1d 1.5.3 | [dav1d-1.5.3.tar.gz](https://github.com/thinkryh/Android_adb/releases/download/v2.0.0-preview.2/dav1d-1.5.3.tar.gz) | `cbe212b02faf8c6eed5b6d55ef8a6e363aaab83f15112e960701a9c3df813686` |

FFmpeg DLL 的实际配置参数见 [FFmpeg 构建记录](FFmpeg-8.1.2-build-config.txt)。这三份源码不覆盖 Android SDK Platform-Tools；ADB 的原始分发条件仍需单独判断。
