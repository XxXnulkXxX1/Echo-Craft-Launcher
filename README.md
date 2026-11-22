# Echo Craft Launcher

一个独立实现的 Minecraft 第三方启动器：Windows 端为原生 C# / WPF 桌面程序，并附带一个体积很小、安装时按需拉取完整程序的网络安装器。

- 版本：**1.0.0**
- 开发者：XxXnulkXxX
- 状态：非官方项目，基于 PCL2 的模块结构思路独立实现，不包含 PCL2 源码

## 下载安装

只需要下载这个很小的安装器（约 142 KB），运行后它会自动拉取完整程序：

- https://github.com/XxXnulkXxX1/Echo-Craft-Launcher/blob/main/dist/EchoCraftSetup.exe

安装器默认把程序装到 `D:\EchoCraftLauncher\Program`，游戏数据统一放在 `D:\EchoCraftLauncher`，重装不会丢存档。

## 功能

- 微软正版登录（OAuth → Xbox Live → XSTS → Minecraft Services），启动前自动刷新令牌
- 离线登录
- 版本清单解析、libraries / assets 下载（官方源与 BMCLAPI 镜像）、natives 解压
- Java 自动检测与手动指定，参数拼装与真实启动
- Modrinth 模组浏览：搜索、平铺分类、图标、应用内详情页、按实例安装及前置依赖
- 模组介绍自动翻译开关（默认关闭，开启后调用翻译服务，可切回原文）
- 主题切换（深/浅）、5 种主色、流动背景、弹簧动效、自定义配色弹窗
- 中 / 英界面切换
- 点击标题栏 X 真正退出进程

## 目录

```
EchoCraftLauncher-Win/       Windows 启动器（C# / WPF，net10.0-windows）
  Pages/                     启动、下载、联机、外观、设置、更多
  Services/                  网络、版本解析、安装器、Java、启动、微软登录、Modrinth、翻译等
  Themes/Theme.xaml          配色与控件样式
  Assets/                    Logo 与 exe 图标
EchoCraftLauncher-Setup/     网络安装器（C# WinForms，net48，单文件约 140 KB）
dist/                        分发件：网络安装器与完整程序包
```

## 构建

需要 .NET 10 SDK（Windows）：

```powershell
cd EchoCraftLauncher-Win
dotnet build -c Release
```

产物在 `bin\Release\net10.0-windows\`。

安装器：

```powershell
cd EchoCraftLauncher-Setup
dotnet build -c Release
```

`SetupForm.cs` 顶部的 `PackageUrl` 指向完整程序包的下载地址，分发前请替换为自己的托管地址。

## 运行时依赖

- .NET 10 Desktop Runtime（安装器会检测并在用户同意后自动安装）
- Microsoft Edge WebView2 Runtime（Win11 / 新版 Win10 一般已自带）

## 说明

- 程序未做代码签名，Windows SmartScreen 可能提示“未知发布者”。
- 游戏数据默认保存在 `D:\EchoCraftLauncher`（无 D 盘时退回用户 AppData）。
- 请支持正版 Minecraft。
