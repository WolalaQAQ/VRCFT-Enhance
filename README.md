# VRCFT-Enhance

[中文](README_zh.md)

Unofficial timing and data-consistency patches for [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking). They reduce losses when the host samples a module's latest tracking state, without changing tracking formulas or introducing another OSC sender.

## Supported versions

Use the package matching the original installation. The two DLLs from different targets are not interchangeable.

| Target | Official source commit | Package suffix |
|---|---|---|
| Steam VRCFT 6.0.0.0, Build 26384174 | [`3d363792`](https://github.com/benaclejames/VRCFaceTracking/tree/3d36379253d26961560d84acecd01d509aea467a) | `vrcft6-3d36379.zip` |
| Legacy VRCFT 5.4.5.0 | [`7add148`](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab) | `7add148.zip` |

VRCFT 6 support starts with VRCFT-Enhance 1.1.0. Earlier patch releases target 5.4.5 only. A future Steam version needs a separately verified patch, even if its assembly version still says 6.0.0.0.

## What changes

- **Polling defaults to 4 ms**, instead of the official 10 ms. The tracking module keeps its own update policy; the patch does not change the PICO module's 2 ms wait.
- **Process-local timer leases** request 1 ms resolution and tell Windows to honor it in the host and module processes. Shutdown restores only the owned timer-policy bit, leaving CPU throttling and priorities alone.
- **Completed snapshots** are published after module Update returns. IPC sends private copies of those bytes rather than reading fields while the module writes them.
- **Host synchronization** covers applying IPC replies and evaluating parameters, including native blob capture. The module send queue is thread-safe, and teardown waits for the Update thread before calling module Teardown.
- **Left outer brow ownership** no longer overlaps: expression modules start after `BrowOuterUpLeft`. An expression-only setup with no eye module consequently no longer updates that brow.

VRCFT 6 keeps the official reply-driven `SendCoordinator`, native `/tracking/face/v1` output, pupil encoding and old-Avatar routing. It does not regain the old periodic sender. With several modules, the configurable output budget is the maximum wait for the remaining modules to reply; with one module, a reply can trigger sending immediately.

The source patches are [VRCFT 6](VRCFT-Enhance-3d36379.patch) and [VRCFT 5](VRCFT-Enhance-7add148.patch). The legacy patch is retained unchanged.

## Measured behavior

A roughly 30-second PC replay used the same recorded PICO eye/mouth payloads, one PICO module, the actual official sandbox and a loopback OSC receiver:

| Steam VRCFT 6 | OSC face messages/s | Distinct eye/mouth sources/s at IPC |
|---|---:|---:|
| Original | 94.74 | 78.27 |
| Patched, default 4 ms | 212.06 | 88.17 |

Input contained 2,694 expression sources at 89.84 sources/s. A higher packet rate includes repeated snapshots; it is not the tracking-source rate. Because 8-bit native encoding maps some different sources to identical values, OSC value matching supports 85.94–88.24 distinct sources/s for the patched run rather than an exact count. This is not a gap-free 90 Hz guarantee or a VRChat rendering measurement.

The comparison measures the whole patch, not polling alone. Both runs selected Bridge data; the original fixture used Auto with an idle Streaming source, while the patched fixture explicitly enabled Bridge only. CPU was not measured in this comparison. Earlier 5.4.5 tests with PICO alone and Cymple-mouth/PICO-eye mixing achieved about 87–88 new eye directions/s; those older CPU and performance figures should not be substituted for VRCFT 6 measurements.

## Install into a separate copy

1. Download the matching `VRCFT-Enhance-<version>-<suffix>.zip` from [Releases](https://github.com/WolalaQAQ/VRCFT-Enhance/releases). If that target is not yet published, build it from source; do not use a different target's package.
2. Close VRCFaceTracking and copy the entire **matching, unmodified installation** to a new directory, such as `%LOCALAPPDATA%\Programs\VRCFaceTracking-Patched`. Leave Steam's original untouched. The legacy package needs an existing 5.4.5 installation, not today's VRCFT 6 folder.
3. Check `provenance.json`, then replace only `VRCFaceTracking.Core.dll` and `VRCFaceTracking.ModuleProcess.dll` in the copy with the matched pair from the package.
4. Run `VRCFaceTracking.exe` from the copy. Keep its SDK, native OSC library and other dependencies from the original matching installation.

The copy shares `%APPDATA%\VRCFaceTracking` modules and settings with the original. Do not run both together. Launch from Steam to return to the original. After a Steam update, make a fresh copy and use a patch verified for that commit instead of overwriting the update with an older patched directory.

## Settings

Positive integer milliseconds, read once at startup; both default to **4**:

| Command line | Process environment | Meaning |
|---|---|---|
| `--osc-poll-ms` | `VRCFT_OSC_POLL_MS` | Interval between module snapshot requests |
| `--osc-output-ms` | `VRCFT_OSC_OUTPUT_MS` | VRCFT 5: periodic output wait. VRCFT 6: maximum mixed-module coalescing wait, **not** a fixed send interval |

Command-line values override environment variables. Both `--osc-poll-ms=4` and `--osc-poll-ms 4` work. For example, append to a shortcut's Target:

```text
--osc-poll-ms=4 --osc-output-ms=4
```

Shorter polling can retain more new sources at a CPU cost. The default preserves the previous 4 ms trade-off; the patch does not queue or reconstruct missed source frames.

## Build and test

Requires Windows, PowerShell 7, Git and the .NET 10 SDK. `tar` is supplied with current Windows versions. From this repository:

```powershell
git clone https://github.com/benaclejames/VRCFaceTracking.git upstream
pwsh -NoProfile -File tools/build.ps1 -Target vrcft6 -UpstreamSource upstream
# For the retained legacy target:
pwsh -NoProfile -File tools/build.ps1 -Target vrcft5 -UpstreamSource upstream
```

The script reads the exact commit from [targets.json](targets.json), archives it into a new build directory, applies the matching patch, builds both DLLs, runs snapshot/ownership/timer/native-protocol tests and command-line settings checks, then packages the matched pair with its install instructions, license and provenance. It does not edit the supplied upstream checkout or install anything. Results are written to a new `artifacts/<target>-<timestamp>/` directory; `-ArtifactsPath` selects another new directory.

CI builds and tests both targets before publishing separately named packages. Only the two patched DLLs are distributed, not a replacement SDK or native OSC library. Patch and upstream versions are also stamped into the DLL product version. The test suite does not cover a real Cymple module or Avatar rendering.

## License

VRCFaceTracking is copyright benaclejames and released under [Apache License 2.0](LICENSE). These patches use the same license; modified upstream files carry modification notices.
