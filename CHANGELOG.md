# Changelog

## [1.0.1] - 2026-10-18

### Features
- Publish a zip with `VRCFaceTracking.Core.dll` and `VRCFaceTracking.ModuleProcess.dll`, built by CI from this patch, so the host can be updated without a local .NET SDK. The zip also contains `LICENSE` and `INSTALL.txt`.

### Changed
- Rename the repository to VRCFT-Enhance. The patch content is unchanged apart from the repository name in the modification notices.

### Design Rationale
- The DLLs are built on a GitHub runner from the same patch that is kept in the repository, and the workflow refuses to publish if the patch does not apply or the build fails.
- The patch stays in the repository so the shipped DLLs can be traced back to readable source.

### Notes & Caveats
- The DLLs only replace the two host files of VRCFaceTracking 5.4.5; other versions are untested.

## [1.0.0] - 2026-10-17

### Features
- Patch for VRCFaceTracking 5.4.5 (`7add148`): configurable request and output waits (default 4 ms, `--osc-poll-ms` / `--osc-output-ms` or `VRCFT_OSC_POLL_MS` / `VRCFT_OSC_OUTPUT_MS`), and a timer resolution lease in the host and module processes.
- Module replies are published as completed snapshots, the host applies replies and evaluates parameters under one lock, the module process uses a thread-safe send queue and stops the Update thread before Teardown.
- Expression modules write from shape 12 onward, so `BrowOuterUpLeft` belongs to the eye module.

### Design Rationale
- The official 10 ms waits stretch to about 15 ms when Windows ignores the process timer resolution. Shorter waits alone are not enough without the lease, and the lease alone reaches about 76 Hz; together they reach about 87–88 Hz on the test PC.
- The consistency fixes were part of the measured build. Shorter waits make the unsynchronized reads in the official code more likely to collide.

### Notes & Caveats
- With only an expression module and no eye module, the left outer brow no longer updates.
- Source patch only; no VRCFaceTracking binaries are distributed.
