# 更新日志

## [1.0.1] - 2026-10-18

### 新功能
- 以 zip 形式发布 `VRCFaceTracking.Core.dll` 和 `VRCFaceTracking.ModuleProcess.dll`，由 CI 从本补丁构建，无需本地安装 .NET SDK 即可更新宿主。压缩包内还包含 `LICENSE` 和 `INSTALL.txt`。

### 变更
- 仓库更名为 VRCFT-Enhance。除修改声明中的仓库名外，补丁内容没有变化。

### 设计理由
- DLL 在 GitHub runner 上从仓库内同一份补丁构建；补丁无法应用或构建失败时，工作流不会发布。
- 补丁保留在仓库中，发布的 DLL 可以追溯到可读的源码。

### 注意事项
- 这两个 DLL 只替换 VRCFaceTracking 5.4.5 的宿主文件；其他版本没有测试过。

## [1.0.0] - 2026-10-17

### Features
- 适用于 VRCFaceTracking 5.4.5（`7add148`）的补丁：请求和输出两处等待可设置（默认 4 ms，`--osc-poll-ms` / `--osc-output-ms` 或 `VRCFT_OSC_POLL_MS` / `VRCFT_OSC_OUTPUT_MS`），宿主和模块进程持有定时器精度租约。
- 模块回包改为发布完整快照；宿主写入回包与计算参数共用一把锁；模块进程的发送队列改为线程安全，并在调用 Teardown 前先停下 Update 线程。
- 表情模块从第 12 号形态键开始写入，`BrowOuterUpLeft` 归眼部模块负责。

### Design Rationale
- Windows 忽略进程定时器精度时，官方的 10 ms 等待会拉长到约 15 ms。只缩短等待而没有租约不够；只开租约约为 76 Hz，两者一起在测试 PC 上约为 87–88 Hz。
- 数据一致性修正包含在实测构建中。等待缩短后，官方代码里没有同步的读写更容易互相干扰。

### Notes & Caveats
- 只开表情模块、不开眼部模块时，左外眉不再更新。
- 只提供源码补丁，不分发 VRCFaceTracking 的二进制文件。
