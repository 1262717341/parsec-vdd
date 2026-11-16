# Parsec 虚拟显示器 · 简体中文版

在上游 [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) v0.45.1（对应已安装的 ParsecVDisplay v0.45.0）基础上做的汉化 + 功能修复版，当前版本 **0.46.9**。

---

## 一、汉化范围

| 位置 | 说明 |
| --- | --- |
| 主窗口 | 标题、`请先添加一个虚拟显示器！`、`添加显示器`、`自定义分辨率…`、按钮提示、版本标签 |
| 托盘 | 托盘提示文字 `Parsec 虚拟显示器 v...`、托盘右键菜单全部条目（添加/移除显示器、开机自动启动、备用显示器、保持屏幕常亮、退出） |
| 显示器右键菜单 | 分辨率、刷新率、屏幕方向（横向/纵向/翻转）、截取屏幕、移除该显示器、`[未激活]` 状态 |
| 自定义分辨率页 | 标题、5 个档位、`应用` 按钮 |
| 所有提示框 | 驱动未安装/被禁用/需重启/状态异常、添加数量超限、退出确认、自定义分辨率校验与权限失败 |
| 驱动状态 | `正常 / 无法访问设备 / 驱动未安装 / 设备已禁用 / 服务已禁用 / 需要重启电脑 / 驱动错误 / 未知设备问题` |
| 程序文件属性 | 文件说明/产品名改为「Parsec 虚拟显示器」 |
| 安装程序 | 中文向导、中文任务说明、中文快捷方式名、中文许可协议（MIT 中文译文 + 英文原文对照） |

---

## 二、修复：开机启动不生效

**原因（已实测确认，两个叠加）**
1. 旧版只有在托盘菜单里手动勾选时才写 `HKCU\...\CurrentVersion\Run`，装包里的“开机启动”是可选任务、默认未勾选 —— 原机器注册表里**根本没有** `ParsecVDisplay` 启动项。
2. 更关键的一条：`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` 里 `ParsecVDisplay` 被写成了 `03 00 00 00 ...`（首字节 bit0 = 1 即“已禁用”，时间戳 **2026-10-24 21:49**，旧版本时期留下的）。只要这个值还是“已禁用”，**哪怕 Run 项存在，开机也会被 Windows 直接跳过**——这也是“托盘勾选了却没启动”的直接原因（任务管理器禁用启动项、安全软件拦截启动项都会写这个值）。
3. 即使写入，被杀软/清理工具删掉后程序不会恢复。
4. 开机瞬间驱动/服务可能尚未就绪：旧版静默启动时若检测到驱动异常会直接退出（静默模式下连提示都没有），表现为“设置了却没用”。

**现在的做法**
1. 安装包默认勾选「开机自动启动」，写入 `"<安装目录>\ParsecVDisplay.exe" -silent`。
2. 程序每次启动都自检启动项（`Config.EnsureStartupEntry`）：Run 项被删会补写；**发现 StartupApproved 是“已禁用”会改回 `02 00 00 00 ...`（启用）**，并在日志里记一条。
3. 托盘菜单的勾选状态 = Run 项存在 **且** 系统未标记禁用；勾选后回读真实状态，写不进去会提示“无法写入开机启动项”。
4. 开机静默启动时，驱动未就绪会等待最多 20 秒再重试，而不是直接弹框退出。
5. 全过程写日志：`%LOCALAPPDATA%\ParsecVDisplay\log.txt`。

> 如果装了火绒等安全软件，请在「启动项管理 / 启动拦截」里把 ParsecVDisplay 设为允许，否则它可能再次把 StartupApproved 改回禁用（程序下次手动运行时仍会自动修回来）。

---

## 三、新增：开机恢复上次的分辨率

- 记录内容：虚拟显示器数量 + 每个显示器的 `宽,高,刷新率,方向`
- 存储位置：`HKLM\SOFTWARE\ParsecVDisplay`（`DisplayCount`，服务开机时读这里）+ `HKCU\SOFTWARE\ParsecVDisplay`（`Display0` … `DisplayN`）
- 保存时机：修改分辨率/刷新率/方向、添加/删除显示器、系统关机、程序正常退出、显示器状态变化
- 恢复流程：启动 → 等驱动就绪 → 按记录补齐显示器数量 → 等显示器出现 → 逐个应用分辨率/刷新率/方向；保存的分辨率已不被驱动支持时保持原样并记日志

---

## 三点五、后台服务化：为什么能解决"启动慢 / 设备 ID 变 / 黑一下"

**驱动机制（源码 `core/parsec-vdd.h` 里写明）**：虚拟显示器由宿主程序每 100ms 内发一次 `Update` 心跳维持，心跳一停显示器就被拔掉。旧版是"界面程序启动才创建显示器、退出就消失"，因此：

- 串流软件（开机自启）先跑起来时，显示器还不存在 → 串流端看不到目标屏；
- 每次开机都要重新"插上"显示器 → 驱动的设备实例重建、Windows 重新分配显示器编号 → 你在串流/系统里看到的 ID 每次都变；
- 插拔显示器会让显示拓扑变化一次，Windows 必须重新协商模式 → 必然黑屏闪一下。

**现在改成了常驻服务**：

| 项目 | 说明 |
| --- | --- |
| 服务名 | `ParsecVDisplaySvc`（`ParsecVDisplaySvc.exe`，**LocalSystem**、`Automatic` 自动启动） |
| 启动时机 | 开机早期，**早于用户登录、早于串流软件** |
| 职责 | 打开驱动句柄 → 按 `HKLM\SOFTWARE\ParsecVDisplay\DisplayCount` 补齐显示器 → 每 80ms 心跳一次，永不拔插 |
| 界面通信 | 本机回环 `127.0.0.1:47523`，命令 `COUNT` / `VERSION` / `ADD` / `REMOVE <i>` / `SET <n>` |
| 界面角色 | 检测到服务就**不再自己持有驱动句柄**，加/删显示器都发给服务执行，只负责本地切换分辨率/方向 |
| 服务日志 | `C:\ProgramData\ParsecVDisplay\svc-log.txt` |

因此：

1. **启动顺序**：显示器在登录前就已存在，串流软件启动时一定能看到它；
2. **设备 ID 稳定**：显示器不再被反复拔插。实测把显示器移除后重新添加，PnP 实例 ID 仍是同一个 `DISPLAY\PSCCDD0\1&28A6823A&9&UID256`，EDID 序列号也固定（`0x00845FED`）；
3. **不黑屏**：登录时没有插拔事件，Windows 直接沿用上次的显示配置；若个别情况下仍被改成别的模式，界面程序会把它改回保存的分辨率（日志里会有"显示器 1: 恢复 …结果：成功"）。只有**首次安装/服务重启**那一次会闪（因为显示器确实被重新插了一次）。
4. 退出主程序**不再拔掉显示器**（服务继续维持），要拔除请在界面里"移除该显示器"。

> 卸载时安装包会自动 `sc stop` + `sc delete` 服务。

---

## 四、编译与打包

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

脚本做四件事：
1. 把 `version.txt` 的版本号写进 `app\App.xaml.cs` 的 `App.VERSION`
2. `dotnet build` 编译（net48 / WPF，不需要装 Visual Studio）
3. Inno Setup 打成中文安装包，**直接输出到桌面**：`ParsecVDisplay-v{版本}-setup.exe`
4. 成功后版本号自动 +1（`0.46.9 → 0.46.10`），下次编译即是新版本

依赖：
- .NET SDK：`%USERPROFILE%\.dotnet\dotnet.exe`
- Inno Setup 6：`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

---

## 五、目录说明

```
app\                 界面源码（C# / WPF）
  App.xaml.cs        版本号、启动流程、驱动等待、启动项自检
  Config.cs          注册表配置：启动项、显示器数量与分辨率记忆
  ParsecVDD.cs       驱动交互 + 开机恢复逻辑（服务模式下走 IPC）
  VddIpc.cs          与服务通信的客户端（127.0.0.1:47523）
  Log.cs             日志
  Components\        主窗口里的显示器卡片、自定义分辨率页
svc\                 常驻服务源码（无界面，开机即保持显示器）
  ParsecVDisplaySvc\
    Program.cs       -test 调试入口 / 服务宿主
    Keeper.cs        驱动句柄 + 心跳 + 数量协调 + 命令端口
    SvcConfig.cs     HKLM\SOFTWARE\ParsecVDisplay 读写
    SvcLog.cs        C:\ProgramData\ParsecVDisplay\svc-log.txt
setup\               Inno Setup 脚本 + 驱动安装包 + 中文语言文件
bin\                 编译产物（ParsecVDisplay.exe + ParsecVDisplaySvc.exe）
backup\              原版 v0.45 的 ParsecVDisplay.exe（回退用）
build.ps1            一键编译打包（两个工程都会编译）
version.txt          下一次编译使用的版本号
```

---

## 六、本机验证结果

- 新版已安装到 `C:\Program Files\ParsecVDisplay`，文件版本 0.46.9，窗口标题「Parsec 虚拟显示器」
- 后台服务 `ParsecVDisplaySvc` 已注册并运行（`Automatic` / LocalSystem），日志：`已连接虚拟显示器驱动`、`已添加虚拟显示器 #1（驱动索引 0）`、`界面命令端口已监听 127.0.0.1:47523`
- 界面检测到服务：日志「检测到常驻服务，虚拟显示器由服务托管」，此时界面不持有驱动句柄
- 命令端口实测：`COUNT → OK 1`、`VERSION → OK 0.45`、`ADD → 2 台显示器`、`REMOVE 1 → 回到 1 台`，Windows 侧显示器数量随之变化
- 设备 ID 稳定性实测：移除后重新添加，PnP 实例 ID 不变（`DISPLAY\PSCCDD0\1&28A6823A&9&UID256`）；EDID 序列号在同一驱动会话内每次新建会 +1，见第七节
- 分辨率恢复实测：安装后显示器被 Windows 恢复成 2560x1600，界面日志「显示器 1: 恢复 1920x1080@60Hz，方向 0，结果：成功」，`EnumDisplaySettings` 读回 `1920x1080@60`，确认生效
- 启动项：`Run` 项 + `StartupApproved\Run = 02 00 00 00…`（已启用）

---

## 七、0.46.8：让虚拟显示器一定先于串流软件（Sunshine）存在

### 1. 问题本质
Sunshine 是开机即启动的系统服务，而旧版虚拟显示器是“用户登录后托盘程序才创建”，所以 Sunshine 初始化时必然看不到虚拟显示器 → 连接报错；等托盘程序再创建时又会重插一次显示器 → 屏幕黑一下、设备身份变化（Sunshine 的 `output_name` GUID 漂移）。

### 2. 本版做法：服务在 `OnStart` 里“卡住”启动流程
Windows 的规则：**只有服务的 `OnStart` 返回，SCM 才会把该服务标记为“正在运行”，依赖它的服务才会被启动。**

因此 `ParsecVDisplaySvc` 变成了一把锁：
1. 开机 → SCM 先启动 `ParsecVDisplaySvc`（`Automatic`）；
2. 服务打开驱动句柄、按配置创建显示器，并每 300ms 用 SetupAPI 枚举 `MONITOR\PSCCDD0` 确认 Windows **真的**认到了显示器；
3. 确认到位后 `OnStart` 才返回（最多等 20 秒，避免超过 SCM 30 秒超时）；
4. 随后 SCM 才启动 `SunshineService` —— 此时虚拟显示器已经在位。

安装包会自动把本服务写入 `HKLM\SYSTEM\CurrentControlSet\Services\SunshineService\DependOnService`（保留原有依赖项），
卸载时自动移除该依赖，避免 Sunshine 因依赖缺失而无法启动。

实现位置：`svc\ParsecVDisplaySvc\Keeper.cs`（`WaitUntilReady` / `ReconcileLocked` / `PingFor`）、`setup\setup.iss` + `setup\sunshine-dependency.ps1`。

### 3. 数量核对改为“以 Windows 实际枚举为准”
`Device.CountPresentDevices(classGuid, hardwareIdPrefix)` 用 SetupAPI（`DIGCF_PRESENT`）统计 `MONITOR\PSCCDD0` 的在位设备数；
`Keeper` 每 3 秒核对一次：少于期望就 `IOCTL_ADD` 补齐（等待期间持续 80ms 心跳，避免驱动把显示器当掉线），多了就 `IOCTL_REMOVE` 减掉。
这样即使驱动返回成功但 Windows 侧没真正挂上，服务也会自己重试补上。

### 4. 设备 ID 与黑屏
实测（本机）：Parsec 驱动在**同一个驱动会话内**每创建一次显示器，EDID 序列号就 +1（`…5F84 00` → `…5F84 EE`），PnP 实例随之新增 `UID257` —— 这就是“设备 ID 每次变”“Sunshine 的 `output_name` 漂移”的根因。
但**驱动会话重置（关掉句柄 / 重启服务 / 重启电脑）后序列号会复位**：服务重启后新建的显示器又回到原身份 `DISPLAY\PSCCDD0\1&28A6823A&9&UID256`、EDID 序列 `ED 5F 84 00`。

结论与对策：
- 只要**每次开机只创建一次、之后不再拔插**，设备 ID 与 `output_name` 就保持稳定；
- 服务常驻正是靠这一点：界面（托盘程序）在服务托管模式下不再自行增删显示器，登录时只负责“恢复分辨率”，所以既不会黑屏，也不会换 ID；
- 手动的“减少到 0 再增加”属于重插，会让 ID 变化一次，日常使用不要这么做。

### 5. 0.46.8 实测记录
- 服务日志：`已连接虚拟显示器驱动` → `等待完成：Windows 已枚举到 1 台虚拟显示器` → `服务已就绪：虚拟显示器已先于串流软件创建完成` → `虚拟显示器已就绪（1 台），串流软件可以连接了`
- 显示器在位：`Get-PnpDevice -Class Monitor -Status OK` 计数 1，实例 `DISPLAY\PSCCDD0\1&28A6823A&9&UID256`，EDID 序列 `ED 5F 84 00`（身份与旧版一致）
- 依赖关系：`(Get-Service SunshineService).ServicesDependedOn` = `ParsecVDisplaySvc (Running)`
- 启动项：`Run = "C:\Program Files\ParsecVDisplay\ParsecVDisplay.exe" -silent`，`StartupApproved\Run` 首字节 `02`（启用）
- 安装包：`<桌面>\ParsecVDisplay-v0.46.8-setup.exe`（2,859,963 字节）

### 6. 重启后的验证方法（最终验收）
重启电脑，然后查看三处：
1. `C:\ProgramData\ParsecVDisplay\svc-log.txt` 应出现 `服务已就绪：虚拟显示器已先于串流软件创建完成`；
2. `C:\Program Files\Sunshine\config\sunshine.log` 中 `Currently available display devices` 列表里，`device_id` 应与 `sunshine.conf` 的 `output_name` 一致，且 `display_name` 是 `\\.\DISPLAY8`；
3. 连接串流应直接落在虚拟显示器上（分辨率 = 你在界面里设定的那台）。

> 说明：`C:\Users\Public\Desktop\ParsecVDisplay.lnk` 是旧版残留的重复快捷方式（新版已生成「Parsec 虚拟显示器」），删除需要管理员权限，可按需手动删掉。

---

## 八、0.46.9：修掉“开机后分辨率记忆被系统改回默认”

### 1. 重启实测（0.46.8）结果
- 服务日志：`服务已就绪：虚拟显示器已先于串流软件创建完成`（04:39:05），Sunshine 到 04:39:13 才枚举显示器 —— **顺序正确，串流软件启动时显示器一定在位**
- Sunshine 日志 `Currently available display devices` 第一条：`device_id = {2bb2012d-f11f-5f14-9f7c-7b8561770d7d}`、`display_name = \\.\DISPLAY8`、`serial_number = 8675309`（= EDID `ED 5F 84 00`）
  与 `sunshine.conf` 的 `output_name` **完全一致** —— 设备 ID 跨重启没有漂移，也没有回退到物理屏
- 显示器实例仍是 `DISPLAY\PSCCDD0\1&28A6823A&9&UID256`（与旧版一致）

### 2. 发现并修掉的新问题：开机把“默认分辨率”写进了记忆
现象：重启后虚拟屏被 Windows 应用成默认 `2560x1600`，而记忆里的 `1920x1080@60` 不见了，注册表 `Display0` 变成 `2560,1600,60,0`。

原因（两条叠加）：
1. Windows 登录后会自己按“每台显示器的记忆设置”再应用一遍显示配置，这会触发界面的 `DisplaySettingsChanged`，而该回调里调用了保存 —— **未经用户同意就把系统默认值写进了记忆**；
2. `RestoreRoutine` 的 `finally` 里也保存了一次，把当时的实际状态（默认值）覆盖了记录。

修法：
- 新增“安静期”：开机后 45 秒内，**系统触发**的分辨率变化不写入记忆（`SaveCurrentDisplays(force=false)` 默认行为）；
- 只有**用户操作**（改分辨率/方向/增减显示器、退出程序、关机）才强制写入（`SaveCurrentDisplays(true)`）；
- `RestoreRoutine` 不再在结束时保存，避免自己覆盖自己；
- 增加“盯梢”：应用完保存的分辨率后等 3 秒复核，若被系统改回去就重新应用一次并写日志「检测到分辨率被系统改回，重新应用上次的设置」；
- 日志里补了一行记录内容，例如「记录中的显示器 1：1920x1080@60Hz，方向 0」，便于排查。

实测（把显示器先弄成 2560x1600、记忆写 1920x1080@60，再启动静默模式）：
```
记录中的显示器 1：1920x1080@60Hz，方向 0
显示器 1: 恢复 1920x1080@60Hz，方向 0，结果：成功
检测到分辨率被系统改回，重新应用上次的设置
已恢复上次的虚拟显示器设置
```
注册表 `Display0` 仍是 `1920,1080,60,0`（没被覆盖），屏幕回到 1920x1080。

### 3. build.ps1 顺手加固
编译前先结束正在运行的 `ParsecVDisplay.exe`，避免 bin 下的 exe 被占用导致 `MSB3027 文件被锁定` 而打包中断（这正是上一次“版本号推进了但桌面没出包”的直接原因）。

### 4. 0.46.9 实测记录
- 已安装到 `C:\Program Files\ParsecVDisplay`，文件版本 0.46.9
- 服务 `Running / Automatic`；`SunshineService` 的依赖仍是 `ParsecVDisplaySvc`
- 显示器在位 1 台（`UID256`），当前模式 1920x1080@60（屏幕报告 1536x864＝125% 缩放后的等效值）
- 启动项 `"C:\Program Files\ParsecVDisplay\ParsecVDisplay.exe" -silent`
- 安装包：`<桌面>\ParsecVDisplay-v0.46.9-setup.exe`（2,860,306 字节）

### 5. “在界面里改成我要的分辨率，下次开机还会变回去吗？”
不会。规则是：**记忆只服从用户操作**。
- 在界面/托盘里改分辨率 → 立刻写入 `HKCU\SOFTWARE\ParsecVDisplay\Display0 = 宽,高,刷新率,方向`；
- 下次开机由 `RestoreRoutine` 读这条记录并应用，应用后还会盯 3 秒、必要时重应用一次，防止被 Windows 的“显示器记忆”改回去；
- 开机后 45 秒内的**系统触发**变化不写记录（避免把默认值写进来），用户操作不受此限制。

实测（2026-11-17 用户把虚拟屏改成 2560x1600 后）：
```
准备恢复上次的虚拟显示器设置，数量：1
  记录中的显示器 1：2560x1600@60Hz，方向 0
已恢复上次的虚拟显示器设置
```
注册表 `Display0 = 2560,1600,60,0`，与实际一致 —— 说明新分辨率已被记住，下次开机按它恢复。

隔离验证（只动虚拟屏，主屏不受影响；用 `ChangeDisplaySettingsEx` 显式指定设备名）：
```
改前:    DISPLAY1=3440x1440@144   DISPLAY8=2560x1600@60   记忆=2560,1600,60,0
只把虚拟屏改成 1920x1080@60 → DISPLAY1=3440x1440@144（未受影响）  DISPLAY8=1920x1080@60
启动程序后 → DISPLAY1=3440x1440@144  DISPLAY8=2560x1600@60   记忆=2560,1600,60,0
日志：显示器 1: 恢复 2560x1600@60Hz，方向 0，结果：成功
```
结论：记忆以用户设置为准，开机按它恢复；用户没改过就不会自行变化。

> 教训：用 `ChangeDisplaySettings`（不带设备名）改虚拟屏会作用到**主显示器**上；要精确修改必须用 `ChangeDisplaySettingsEx("\\.\DISPLAYn", …)`。本机曾经因此误改主屏，已恢复为 3440x1440@144Hz。
建议：改分辨率优先用本程序的界面（下拉列表），这样一定会记录；用 Windows 自带“显示设置”改也大多能记录，但开机后 45 秒内改的可能不被记录。
