# VRCFT-Enhance

[English](README.md)

这是面向 [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) 的非官方定时与数据一致性补丁，用于减少宿主抽取模块最新状态时遗漏的追踪源，不改变追踪公式，也不增加第二个 OSC 发送器。

## 支持版本

选择与原安装匹配的包，不要混用不同目标的两个 DLL。

| 目标 | 官方源码提交 | 安装包后缀 |
|---|---|---|
| Steam VRCFT 6.0.0.0，Build 26384174 | [`3d363792`](https://github.com/benaclejames/VRCFaceTracking/tree/3d36379253d26961560d84acecd01d509aea467a) | `vrcft6-3d36379.zip` |
| 旧版 VRCFT 5.4.5.0 | [`7add148`](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab) | `7add148.zip` |

VRCFT 6 支持从 VRCFT-Enhance 1.1.0 开始，更早的补丁只适用于 5.4.5。以后 Steam 更新需要另行核对，即使程序集版本仍显示 6.0.0.0，也不能直接混用。

## 补丁改了什么

- **宿主轮询默认为 4 ms**，替代原来的 10 ms。模块自身的更新策略不变，不修改 PICO 模块原有的 2 ms 等待。
- **进程内定时器租约**在宿主和模块进程申请 1 ms 精度，并要求 Windows 尊重该请求。退出时只恢复自己持有的定时策略位，不改 CPU 节流或优先级。
- **模块返回完整快照**：Update 结束后打包并发布，IPC 发送其独立副本，不在模块写入过程中逐字段读取。
- **宿主数据同步**覆盖 IPC 回包应用与参数计算，包括原生 blob 捕获；模块发送队列改为线程安全，Teardown 前先等待 Update 线程结束。
- **左外眉不再重叠写入**：表情模块从 `BrowOuterUpLeft` 之后开始写。只开表情模块、不开眼部模块时，该眉因此不再由表情模块更新。

VRCFT 6 保留官方收到回复触发发送的 `SendCoordinator`、原生 `/tracking/face/v1`、瞳孔编码和旧 Avatar 路由，不恢复旧版定时发送循环。混搭时，输出等待参数控制等待其余模块回复的最长时间；只有一个模块时，可在回复到达后立即触发发送。

源码补丁分别为 [VRCFT 6](VRCFT-Enhance-3d36379.patch) 与 [VRCFT 5](VRCFT-Enhance-7add148.patch)，旧补丁内容保持不变。

## 实测表现

一次约 30 秒的 PC 回放使用同一份 PICO 眼嘴录制负载、单 PICO 模块、真实官方沙盒和环回 OSC 接收器：

| Steam VRCFT 6 | 每秒 OSC 脸部消息 | IPC 中每秒独立眼嘴新源 |
|---|---:|---:|
| 原版 | 94.74 | 78.27 |
| 补丁，默认 4 ms | 212.06 | 88.17 |

输入有 2694 个表情源，约 89.84 个新源／秒。较高发包率包含重复快照，不等于追踪新源率。原生 8-bit 编码会把部分不同源量化成相同数值，因此按 OSC 值归源，补丁轮可验证为 85.94–88.24 个独立源／秒，而不是精确计数。这不是每帧无缺口的 90 Hz 保证，也不是 VRChat 渲染帧率。

对照测量的是整套补丁，不是仅改变轮询常量。两轮都选用 Bridge 数据；原版测试以 Auto 锁定 Bridge，另有空闲 Streaming 输入源，补丁测试则明确只启用 Bridge。本轮没有测 CPU。此前 5.4.5 的单 PICO 与 Cymple 嘴＋PICO 眼回放约有 87–88 个新眼部方向／秒，不能把旧版的 CPU 或性能数字替代本次 VRCFT 6 实测。

## 安装到独立副本

1. 从 [Releases](https://github.com/WolalaQAQ/VRCFT-Enhance/releases) 下载匹配的 `VRCFT-Enhance-<版本>-<后缀>.zip`。目标包尚未发布时可自行构建，不要改用其他目标的包。
2. 关闭 VRCFaceTracking，把完整、**未修改且版本匹配**的安装目录复制到新位置，例如 `%LOCALAPPDATA%\Programs\VRCFaceTracking-Patched`，Steam 原件保持不动。旧版包需要已有的 5.4.5 安装，不能搭配目前的 VRCFT 6 目录。
3. 核对 `provenance.json`，再用包内配套的 `VRCFaceTracking.Core.dll` 和 `VRCFaceTracking.ModuleProcess.dll` 替换副本中的同名文件。
4. 从副本运行 `VRCFaceTracking.exe`，SDK、原生 OSC 库及其他依赖保留匹配的官方安装版本。

副本与原版共用 `%APPDATA%\VRCFaceTracking` 中的模块和设置，不要同时运行。从 Steam 启动即可回到原版。Steam 升级后应重新复制正式目录，使用对应提交的补丁，不要把旧补丁目录直接覆盖到新版安装上。

## 设置等待时间

数值为正整数毫秒，只在启动时读取，默认均为 **4**：

| 命令行参数 | 进程环境变量 | 含义 |
|---|---|---|
| `--osc-poll-ms` | `VRCFT_OSC_POLL_MS` | 模块快照请求间隔 |
| `--osc-output-ms` | `VRCFT_OSC_OUTPUT_MS` | VRCFT 5：定时输出等待。VRCFT 6：混搭回复最长合并等待，**不是**固定发送周期 |

命令行优先于环境变量，支持 `--osc-poll-ms=4` 和 `--osc-poll-ms 4` 两种写法。例如在快捷方式的“目标”末尾追加：

```text
--osc-poll-ms=4 --osc-output-ms=4
```

更短的轮询可能保留更多新源，也会增加 CPU 开销。默认沿用先前 4 ms 的取舍；补丁不为遗漏的源帧排队或重建数据。

## 构建与测试

需要 Windows、PowerShell 7、Git 和 .NET 10 SDK，当前 Windows 已提供 `tar`。在本仓库执行：

```powershell
git clone https://github.com/benaclejames/VRCFaceTracking.git upstream
pwsh -NoProfile -File tools/build.ps1 -Target vrcft6 -UpstreamSource upstream
# 保留的旧版目标：
pwsh -NoProfile -File tools/build.ps1 -Target vrcft5 -UpstreamSource upstream
```

脚本从 [targets.json](targets.json) 读取固定提交，导出到新构建目录，应用对应补丁，构建两个 DLL，通过快照／所有权／定时器／原生协议及命令行设置测试后打包。包内包含配套 DLL、安装说明、许可证和来源记录。脚本不修改传入的上游工作树，也不安装程序。结果保存在新的 `artifacts/<目标>-<时间戳>/`；`-ArtifactsPath` 可指定另一个新目录。

CI 在发布前分别构建和测试两个目标，生成不同名称的安装包，只分发两个补丁 DLL，不替换 SDK 或原生 OSC 库。DLL 产品版本同时记录补丁版本和上游提交。测试不包含真实 Cymple 模块或 Avatar 渲染。

## 许可证

VRCFaceTracking 版权归 benaclejames，使用 [Apache License 2.0](LICENSE)。本补丁采用同一许可证，修改过的上游文件均带修改说明。
