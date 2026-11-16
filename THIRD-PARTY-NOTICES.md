# 第三方组件与许可声明（Third-Party Notices）

本仓库（`parsec-vdd-zh`，Parsec 虚拟显示器 · 简体中文版）是在开源项目 [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) 基础上的**非官方改造版**。上游版权与本许可条款如下，改动部分同样以 MIT 发布。

## 随仓库分发

| 组件 | 位置 | 用途 | 许可证 | 版权 | 上游 |
| --- | --- | --- | --- | --- | --- |
| nomi-san/parsec-vdd v0.45.1 | `app/`、`svc/` 的二次开发基础；`setup/` 中的驱动安装器引用 | 虚拟显示器驱动接口、宿主程序与设备管理 | MIT | © 2023 Nguyen Duy &lt;wuuyi123@gmail.com&gt; | [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) |
| Parsec VDD 驱动（`parsec-vdd-setup.exe`） | 安装包内 `{app}\driver\` | 创建虚拟显示器设备 | 上游 MIT 发行物（含其签名驱动） | © 2023 Nguyen Duy | 同上 |

许可全文：`third_party/parsec-vdd-LICENSE.txt`（与本仓库根目录 `LICENSE` 内容一致）。

> 驱动安装包**未**收录进本仓库（见 `.gitignore` 的 `setup/parsec-vdd-setup.exe`）；打包前请从上游发行包取得，本项目不重新签名、不单独分发该驱动。

## 仅构建期使用

| 组件 | 用途 | 许可证 | 版权 | 上游 |
| --- | --- | --- | --- | --- |
| Inno Setup 6 | 生成中文安装程序（安装包内含其安装引擎） | Inno Setup License（允许免费分发由它生成的安装包） | © 1997–2024 Jordan Russell | [jrsoftware.org/isinfo.php](https://jrsoftware.org/isinfo.php) |
| .NET SDK / WPF | 编译 `net48` 目标 | MIT（.NET 运行时与 SDK） | © .NET Foundation | [dotnet.microsoft.com](https://dotnet.microsoft.com) |

## 声明

- 本仓库是**社区第三方中文改造版**，未获上游作者 nomi-san 的官方背书或赞助；上游项目自身的 README、文档与发布节奏与本仓库无关。
- 本项目与 **Parsec Cloud, Inc.** 无任何隶属关系。"Parsec" 是 Parsec Cloud, Inc. 的商标，此处仅沿用上游对 "Parsec Virtual Display Driver" 的称呼，不代表官方产品或授权。
- 上游 MIT 许可要求保留版权与许可声明；本仓库保持 `LICENSE` 原文不变，并在 README 与本文中标注来源。
- 如你是权利人并认为此处归属有误，请提交 issue，我们会及时更正。
