# Parsec 虚拟显示器 · 简体中文版

基于上游 [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) v0.45.1 的**汉化 + 功能改造**版本：中文界面与安装向导，修好开机自启，新增分辨率记忆，并把虚拟显示器的创建搬进常驻服务，解决串流玩家最痛的三个问题——串流软件启动时看不到虚拟屏、设备 ID 每次开机都变、开机黑一下。

- 当前版本：**v0.46.9**（MIT License）
- 平台：Windows 10 / 11 x64
- 技术栈：C# / WPF（net48）+ 常驻 Windows 服务 + Inno Setup 安装包

## 下载安装

到 [Releases](https://github.com/1262717341/parsec-vdd-zh/releases/latest) 下载 `ParsecVDisplay-v0.46.9-setup.exe`（约 2.9 MB），安装向导为中文，默认勾选「开机自动启动」并注册后台服务 `ParsecVDisplaySvc`。

> 驱动仍使用上游的签名驱动包 `parsec-vdd-setup.exe`，安装时由安装程序自动调用（默认已勾选）。

## 相对上游 v0.45 的改动

| 改动 | 说明 |
| --- | --- |
| **完整汉化** | 主窗口、托盘与右键菜单、自定义分辨率页、各类提示框、驱动状态文案、程序文件属性、安装向导与许可协议（MIT 中英对照） |
| **修好开机自启** | 旧版只在手动勾选时才写启动项；新版安装默认勾选，且每次启动自检 `Run` 项与 `StartupApproved\Run`，被安全软件标记禁用会自动改回启用 |
| **新增分辨率记忆** | 记录显示器数量与每台的 `宽,高,刷新率,方向`（`HKLM/HKCU\SOFTWARE\ParsecVDisplay`），开机按它恢复；只有用户操作才写记忆，开机 45 秒内系统触发的变更不写，恢复后还会盯 3 秒必要时重应用 |
| **常驻服务化** | 新增 `ParsecVDisplaySvc`（LocalSystem / Automatic）：开机即打开驱动句柄、按配置补齐显示器、每 80ms 心跳，显示器不再被反复拔插 → **设备 ID 稳定、登录不黑屏、退出界面不掉屏** |
| **串流启动顺序** | 服务的 `OnStart` 会等到 Windows 真的枚举到 `MONITOR\PSCCDD0` 才返回（最多 20 秒），并把本服务写入 `SunshineService` 的 `DependOnService`，保证串流软件初始化时虚拟屏已在位，卸载时自动摘除依赖 |
| **数量以系统为准** | 服务每 3 秒用 SetupAPI 统计在位显示器数，少于期望就补、多了就减，不依赖驱动的返回值 |

## 从源码构建

前置条件：

- Windows 10 / 11 x64
- .NET SDK（编译 `net48` / WPF，不需要装 Visual Studio）
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)
- 上游驱动安装包 `parsec-vdd-setup.exe` 放到 `setup\`（受体积与分发考虑未入库，可从上游项目获取）

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

脚本会：把 `version.txt` 的版本号写进 `app\App.xaml.cs` → 编译主程序与常驻服务到 `bin\` → 调 Inno Setup 打包到桌面（`ParsecVDisplay-v{版本}-setup.exe`）→ 成功后版本号自动 +1。

## 目录结构

```
app\                  界面源码（C# / WPF）
  App.xaml.cs         版本号、启动流程、驱动等待、启动项自检
  Config.cs           注册表配置：启动项、显示器数量与分辨率记忆
  ParsecVDD.cs        驱动交互 + 开机恢复逻辑（服务模式下走 IPC）
  VddIpc.cs           与服务通信的客户端（127.0.0.1:47523）
  Components\         显示器卡片、自定义分辨率页
svc\ParsecVDisplaySvc\  常驻服务（无界面）
  Keeper.cs           驱动句柄 + 心跳 + 数量协调 + 命令端口
  SvcConfig.cs        HKLM\SOFTWARE\ParsecVDisplay 读写
  SvcLog.cs           日志 C:\ProgramData\ParsecVDisplay\svc-log.txt
setup\                Inno Setup 脚本、中文语言文件、中文许可、Sunshine 依赖脚本
docs\DEVLOG.md        逐版本实测与排障记录（含踩坑结论）
build.ps1             一键编译 + 打包
version.txt           下一次编译使用的版本号
```

## 已知限制

- **首次安装或服务重启**时虚拟屏会被重新插一次，屏幕仍会闪一下；之后登录不会。
- 手动把显示器“减到 0 再增加”属于重插，会让设备 ID 变化一次，日常使用不要这么做。
- 驱动与签名仍来自上游，本项目只改宿主程序与安装/服务逻辑。
- 本版本不改写手机/系统级设置；分辨率记忆写入的是当前用户与机器的注册表项。

## 许可与致谢

- 本项目及其改动以 **MIT License** 发布，见 [LICENSE](LICENSE)；原始版权归 Nguyen Duy (2023)。
- 上游：[nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd)（MIT）——驱动、`core` 头文件与整体思路来源。
- 安装包使用 [Inno Setup](https://jrsoftware.org/isinfo.php)。

> 各版本的真实重启验证、日志片段与踩坑过程（含“改指定显示器必须用 `ChangeDisplaySettingsEx`”这一类教训）见 [docs/DEVLOG.md](docs/DEVLOG.md)。
