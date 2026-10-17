# 更新日志

## [1.0.1] - 2026-10-17

### 变更
- 仓库更名为 VRCFT-Enhance。除修改声明中的仓库名外，补丁内容没有变化。

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
