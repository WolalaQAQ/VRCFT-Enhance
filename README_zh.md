# osc90-host-patch

[English](README.md)

这是一个基于 [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) 的源码补丁，作用是让 VRCFaceTracking 宿主把眼追数据以接近 90 Hz 的频率交给 OSC。它不是 VRCFaceTracking 官方项目，也不提供编译好的程序；需要自己打补丁并构建，然后放进一份独立的 VRCFaceTracking 副本里使用，Steam 版保持原样。

补丁对应 VRCFaceTracking 5.4.5，也就是 Steam 正式版所用的源码提交 [`7add148`](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab)（程序集版本 5.4.5.0）。其他版本没有验证过。

## 为什么需要它

官方宿主有两处固定的 10 ms 等待：一处定时向模块请求最新数据，另一处定时计算参数并发送 OSC。理论上这能达到 100 Hz，但 Windows 11 可能会忽略进程申请的 1 ms 定时器精度，这时 10 ms 等待实际要 15 ms 左右，请求频率只剩约 64 Hz，眼追数据中间的许多帧就被跳过了。

我们用 PICO 4 Pro 通过 Virtual Desktop 串流做了测试：按原来的 90 Hz 节奏回放 2700 帧已录制的眼部画面，统计 OSC 输出里新的眼睛方向数。

| VRCFaceTracking 宿主 | 每秒新的眼睛方向 |
|---|---:|
| 官方原版 | 约 61–78 Hz |
| 打补丁，默认 4 ms 等待 | 约 87–88 Hz |

官方原版每次的结果起伏较大，主要取决于 Windows 当时是否遵守定时器精度。补丁的测试包括“Cymple 出嘴部、PICO 出眼部”的混搭和只用 PICO 两种情况，每种跑一轮约 30 秒的回放；宿主和模块进程合计占用约 12–17% 个逻辑核。测试用的构建另外带有诊断记录，以及只为测试环境准备的启动和 OSCQuery 改动；与定时和数据交换相关的代码与本补丁相同。

测试 PC 为 Ryzen 7 9700X、RTX 4070 Ti SUPER、64 GB 内存，Windows 11 专业工作站版，卓越性能电源计划。其他电脑上的结果可能不同。

## 补丁改了什么

### 定时与等待

- **等待时间可设置**：两处 10 ms 等待改为可设置，默认 4 ms。设置方法见[设置等待时间](#设置等待时间)。
- **定时器精度租约**：宿主进程和每个模块进程启动时，告诉 Windows 不要忽略本进程申请的 1 ms 定时器精度，同时申请 1 ms 精度；退出时只恢复这一项设置，其他电源策略不动。官方代码只申请 1 ms 精度，Windows 仍可能忽略它。在官方宿主上只开启这一项，就能从约 61 Hz 升到约 76 Hz（测试时 PICO 模块进程已由模块自己开启这一项）；再缩短等待，才接近 90 Hz。

### 数据一致性

把等待缩短后，几个线程交换数据的次数变多，官方代码里几处没有同步的读写也更容易互相干扰，所以补丁一并做了修正：

- **模块回包使用完整快照**：模块每次 Update 结束后，先把这一帧的数据打包好；宿主来请求时直接返回这份包。原来是在请求到达时现场逐个字段读取，可能读到前半是旧帧、后半是新帧的数据。
- **宿主计算参数时加锁**：宿主写入模块回包和计算 OSC 参数轮流进行，不会边写边算。
- **模块进程的发送队列改为线程安全**：原来两个线程同时读写一个普通 `Queue`，现在改用 `ConcurrentQueue`；模块关闭时先等 Update 线程停下，再调用模块的 Teardown。
- **眼部与表情的分界**：官方合并数据时，眼部模块和表情模块都会写第 11 号形态键 `BrowOuterUpLeft`（左外眉），同时开两个模块时后到的会覆盖先到的。补丁让表情模块从第 12 号开始写，左外眉只由眼部模块负责。

最后一项会改变一种用法的结果：如果只开一个表情模块、不开眼部模块，原来由表情模块提供的左外眉将不再更新。同时使用眼部和表情模块，或只用一个同时提供两者的模块时，没有这个影响。

补丁修改了 6 个官方文件，新增 `OscWaitBudgets.cs` 和 `TimerResolutionLease.cs` 两个文件。每个被修改的文件第一行都加了修改说明。

## 构建

需要 Windows、[Git](https://git-scm.com/) 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

1. 获取官方源码并切换到对应提交：

   ```powershell
   git clone https://github.com/benaclejames/VRCFaceTracking.git
   git -C VRCFaceTracking checkout 7add14873e398c4095d06913cdeee7a01fc43fab
   ```

2. 应用补丁（假设补丁文件与 `VRCFaceTracking` 目录在同一层）：

   ```powershell
   git -C VRCFaceTracking apply --ignore-whitespace ..\osc90-host-patch-7add148.patch
   ```

   `--ignore-whitespace` 用来兼容 Git 检出时的不同换行设置。

3. 构建：

   ```powershell
   dotnet build VRCFaceTracking\VRCFaceTracking.ModuleProcess\VRCFaceTracking.ModuleProcess.csproj -c Release -p:Platform=x64
   ```

   需要的两个文件 `VRCFaceTracking.Core.dll` 和 `VRCFaceTracking.ModuleProcess.dll` 在 `VRCFaceTracking\VRCFaceTracking.ModuleProcess\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\` 里。

## 安装到独立副本

1. 关闭 VRCFaceTracking。
2. 把整个 Steam 安装目录（默认 `C:\Program Files (x86)\Steam\steamapps\common\VRCFaceTracking`）复制到一个新位置，例如 `%LOCALAPPDATA%\Programs\VRCFaceTracking-Patched`。
3. 用构建出的 `VRCFaceTracking.Core.dll` 和 `VRCFaceTracking.ModuleProcess.dll` 替换副本里的同名文件。
4. 运行副本里的 `VRCFaceTracking.exe`。

副本和 Steam 版共用 `%APPDATA%\VRCFaceTracking` 里的模块和设置，不需要重新安装模块。两者不要同时运行。想换回官方版，直接从 Steam 启动即可；Steam 更新也不会改动副本。

## 设置等待时间

两处等待可以分别设置，单位为毫秒，必须是正整数，默认都是 4：

| 命令行参数 | 环境变量 | 作用 |
|---|---|---|
| `--osc-poll-ms` | `VRCFT_OSC_POLL_MS` | 向模块请求最新数据的间隔 |
| `--osc-output-ms` | `VRCFT_OSC_OUTPUT_MS` | 计算参数并发送 OSC 的间隔 |

命令行参数优先于环境变量。参数可以写成 `--osc-poll-ms=4` 或 `--osc-poll-ms 4`，例如在快捷方式的“目标”末尾加上：

```text
--osc-poll-ms=4 --osc-output-ms=4
```

数值只在启动时读取，修改后需要重启 VRCFaceTracking。等待越短，频率越接近模块的实际输出，CPU 占用也越高；我们的测试中 4 ms 与 5 ms 的结果相差很小。

## 许可证

VRCFaceTracking 以 [Apache License 2.0](LICENSE) 发布，版权归 benaclejames 所有。本补丁同样以 Apache License 2.0 发布，[LICENSE](LICENSE) 为原文。
