# Android 投屏助手

面向 Windows 的 Android 投屏工具。图形版使用 WPF 管理设备和连接，实际投屏仍由仓库内的 scrcpy 与 ADB 完成。项目保留 v1 批处理／PowerShell 流程作为图形版不可用时的回退入口。

> 仓库提供 v2 源码及 scrcpy／ADB 组件；`bin/` 和 `obj/` 不提交到 Git。**仅从 GitHub 克隆仓库，不会得到已构建的 v2 图形版可执行文件。**请先构建，或另取完整发布包。详见[文件状态](docs/文件状态.md)。

## 现在能做什么

- 识别 USB、IP 无线、mDNS 和模拟器设备，显示连接／授权／投屏状态；每台设备独立启动投屏窗口。
- 提供 IP 直连、六位配对码配对、二维码配对入口与 mDNS 服务发现；无设备时尝试最近保存的无线连接地址。
- 无线默认均衡画质；快捷栏可吸附、自由拖动、收起，并提供置顶、主页、返回、最近任务、截图、录屏。
- 默认黑夜模式，可在主界面切换浅色模式；截图和录屏保存到当前用户的“图片\Android投屏助手”。

功能边界、目标和验收口径见[功能与目标](docs/功能与目标.md)；连接条件、目录、进程和构建方式见[架构与约束](docs/架构与约束.md)。[验证与问题记录](docs/验证与问题记录.md)保留已经试过的方法、失败结果和仍需验证的场景。

## 运行与构建

在 Windows 10／11 上，保持整个项目目录完整，双击根目录的 `Android投屏助手.bat`。脚本会优先查找本机 v2 发布程序；找不到时使用 `程序组件/Android投屏助手.ps1` 的 v1 流程。不要单独复制 `.bat`。手机须开启 USB 调试并授权；无线调试还要求手机和电脑处于可互通网络。

从源码运行 v2 需要 .NET 10 SDK；当前发布配置为**依赖框架**，目标电脑运行 v2 还需要 .NET 10 Desktop Runtime。项目目录下执行：

```powershell
dotnet restore .\Android投屏助手_v2\Android投屏助手.csproj
dotnet publish .\Android投屏助手_v2\Android投屏助手.csproj -c Release --self-contained false -o .\Android投屏助手_v2\bin\Release\net10.0-windows\glass-v13
```

构建时项目会把上级 `scrcpy/` 复制进输出目录。启动脚本会优先使用 `glass-v13`；如采用其他输出目录，请先核对启动脚本的候选顺序。`Android投屏助手_v2/Android投屏助手_v2.bat` 是旧的开发入口，未列入最新 `glass-v13` 查找路径。

## 目录

```text
Android投屏助手_v1/
├─ Android投屏助手.bat          当前总入口，v2 优先、v1 回退
├─ Android投屏助手_v2/          WPF 源码、项目文件和本地构建输出
├─ 程序组件/                    v1 PowerShell 启动脚本
├─ scrcpy/                     随仓库提供的 scrcpy、ADB 与依赖文件
├─ docs/                       产品、技术、验证与文件状态
└─ Android投屏助手使用说明.txt  简明使用说明
```

`scrcpy`、ADB 和 QRCoder 为第三方组件，分发时应保留相应版权及许可证信息；仓库内附有 `scrcpy/LICENSE.txt`。本项目的玻璃外观是 WPF 样式实现，不等同于 iOS 的系统级 Liquid Glass 材质。
