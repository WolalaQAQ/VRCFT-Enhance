# VRCFT-Enhance

[中文](README_zh.md)

Unofficial patches for [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking). The current patch lets the host pass eye-tracking data to OSC at close to 90 Hz by making its update rate configurable, and fixes a few threading issues that show up when it does.

It is not an official VRCFaceTracking project. The releases contain the patched host DLLs in a zip. The source patch is kept in the repository for anyone who wants to build it themselves.

The patch targets VRCFaceTracking 5.4.5, the source commit used by the Steam release, [`7add148`](https://github.com/benaclejames/VRCFaceTracking/tree/7add14873e398c4095d06913cdeee7a01fc43fab) (assembly version 5.4.5.0). Other versions have not been tested.

## Why

The official host has two fixed 10 ms waits: one for requesting the latest data from each module, and one for evaluating parameters and sending OSC. On paper that is 100 Hz, but Windows 11 may ignore the 1 ms timer resolution the process asks for. When it does, each 10 ms wait takes about 15 ms, requests drop to about 64 Hz, and many eye frames are skipped.

We tested this on a PICO 4 Pro streaming through Virtual Desktop, replaying 2,700 recorded eye frames at their original 90 Hz timing and counting new eye directions at the OSC output:

| VRCFaceTracking host | New eye directions per second |
|---|---:|
| Official, unmodified | about 61–78 Hz |
| Patched, default 4 ms waits | about 87–88 Hz |

The official host varies a lot from run to run, mostly depending on whether Windows honors the timer resolution at the time. The patched host was tested both with Cymple providing the mouth and PICO the eyes, and with PICO alone, one replay of about 30 seconds each; the host and module processes together used about 12–17% of one logical core. The test build also carried diagnostic logging and startup and OSCQuery changes needed only by the test setup; its timing and data-exchange code is the same as this patch.

The test PC had a Ryzen 7 9700X, RTX 4070 Ti SUPER, 64 GB RAM and Windows 11 Pro for Workstations on the Ultimate Performance power plan. Other PCs may see different numbers.

## What the patch changes

### Timing

- **Configurable waits**: the two 10 ms waits become settings, 4 ms by default. See [Setting the waits](#setting-the-waits).
- **Timer resolution lease**: when the host and each module process start, they tell Windows not to ignore the process's 1 ms timer resolution request, and then request 1 ms. On exit only that setting is restored; other power policies are left alone. The official code only requests 1 ms, which Windows may still ignore. Turning this on for the official host alone raised it from about 61 Hz to about 76 Hz (the PICO module process had already turned it on for itself); shorter waits are what bring it close to 90 Hz.

### Data consistency

Shorter waits mean threads exchange data more often, so a few unsynchronized reads and writes in the official code are more likely to collide. The patch fixes them:

- **Module replies use a completed snapshot**: after each module Update, the frame is packed once, and host requests receive that packet. Previously the fields were read one by one when a request arrived, which could mix half of an old frame with half of a new one.
- **Locked parameter evaluation**: the host applies module replies and evaluates OSC parameters in turn, never both at once.
- **Thread-safe send queue in the module process**: two threads used to read and write a plain `Queue`; it is now a `ConcurrentQueue`. When a module shuts down, the Update thread is stopped before the module's Teardown is called.
- **Eye/expression boundary**: when merging data, the official host lets both the eye module and the expression module write shape 11, `BrowOuterUpLeft` (left outer brow), so with two modules running, one overwrites the other. The patch makes the expression module start at shape 12, leaving the left outer brow to the eye module.

The last change affects one setup: if you run only an expression module and no eye module, the left outer brow that the expression module used to provide will no longer update. It has no effect when you use both an eye and an expression module, or a single module that provides both.

The patch modifies 6 official files and adds two new files, `OscWaitBudgets.cs` and `TimerResolutionLease.cs`. Each modified file carries a modification notice on its first line.

## Installing into a separate copy

1. Download `VRCFT-Enhance-<version>-7add148.zip` from [Releases](https://github.com/WolalaQAQ/VRCFT-Enhance/releases) and extract it.
2. Close VRCFaceTracking.
3. Copy the whole Steam installation folder (by default `C:\Program Files (x86)\Steam\steamapps\common\VRCFaceTracking`) to a new location, for example `%LOCALAPPDATA%\Programs\VRCFaceTracking-Patched`.
4. Replace `VRCFaceTracking.Core.dll` and `VRCFaceTracking.ModuleProcess.dll` in the copy with the two files from the zip.
5. Run `VRCFaceTracking.exe` from the copy.

The copy shares modules and settings with the Steam version through `%APPDATA%\VRCFaceTracking`, so there is no need to reinstall modules. Do not run both at the same time. To go back to the official version, launch it from Steam; Steam updates do not touch the copy.

The DLLs are built for VRCFaceTracking 5.4.5 only. Replacing them in another version is untested and may prevent the host from starting.

## Building from source (optional)

You need Windows, [Git](https://git-scm.com/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). GitHub Actions runs these steps on every push, so the patch is known to apply and build before a release is published.

1. Get the official source and check out the matching commit:

   ```powershell
   git clone https://github.com/benaclejames/VRCFaceTracking.git
   git -C VRCFaceTracking checkout 7add14873e398c4095d06913cdeee7a01fc43fab
   ```

2. Apply the patch (assuming the patch file sits next to the `VRCFaceTracking` folder):

   ```powershell
   git -C VRCFaceTracking apply --ignore-whitespace ..\VRCFT-Enhance-7add148.patch
   ```

   `--ignore-whitespace` makes the patch apply regardless of Git's line-ending settings at checkout.

3. Build:

   ```powershell
   dotnet build VRCFaceTracking\VRCFaceTracking.ModuleProcess\VRCFaceTracking.ModuleProcess.csproj -c Release -p:Platform=x64
   ```

   The two files you need, `VRCFaceTracking.Core.dll` and `VRCFaceTracking.ModuleProcess.dll`, are in `VRCFaceTracking\VRCFaceTracking.ModuleProcess\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\`. That is a Release build; use it the same way as the files in the zip.

## Setting the waits

Each wait can be set separately, in milliseconds, as a positive integer. Both default to 4:

| Command-line option | Environment variable | Controls |
|---|---|---|
| `--osc-poll-ms` | `VRCFT_OSC_POLL_MS` | Interval between requests for the latest module data |
| `--osc-output-ms` | `VRCFT_OSC_OUTPUT_MS` | Interval between parameter evaluation and OSC sends |

Command-line options take precedence over environment variables. Write them as `--osc-poll-ms=4` or `--osc-poll-ms 4`, for example at the end of a shortcut's Target:

```text
--osc-poll-ms=4 --osc-output-ms=4
```

The values are read once at startup, so restart VRCFaceTracking after changing them. Shorter waits follow the module's real output more closely and use more CPU; in our tests 4 ms and 5 ms gave nearly the same result.

## License

VRCFaceTracking is released under the [Apache License 2.0](LICENSE), copyright benaclejames. This patch is also released under the Apache License 2.0; [LICENSE](LICENSE) is the full text.
