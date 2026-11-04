# Changelog

## [1.1.0] - 2026-11-05

### Features
- Add a separate patch for Steam VRCFT 6.0.0.0, Build 26384174, commit `3d36379253d26961560d84acecd01d509aea467a`; retain the existing 5.4.5 patch unchanged.
- Port the 4 ms polling default, process-local timer leases, completed IPC snapshots, synchronized host evaluation, concurrent module send queue and non-overlapping left-outer-brow ownership.
- Add pinned two-target builds, patch tests, settings precedence/invalid-value checks and version-specific packages with provenance records.

### Design Rationale
- VRCFT 6 already sends on module replies. Preserve that coordinator and native/legacy Avatar routing; interpret `--osc-output-ms` as its maximum mixed-module coalescing wait instead of adding the old periodic sender.
- Keep Core and ModuleProcess paired while retaining the matching official SDK and native encoder. Publish each target separately rather than treating the common DLL filenames as cross-version compatibility.

### Notes & Caveats
- In one roughly 30-second single-PICO PC payload replay, independent IPC eye/mouth sources increased from 78.27 to 88.17/s. Native OSC messages increased from 94.74 to 212.06/s, including repeated snapshots; quantized OSC source matching supports 85.94–88.24 sources/s for the patched run. This is not guaranteed gap-free 90 Hz or a rendering test.
- The comparison covers the patch bundle, not an isolated polling change. Real Cymple/VRCFT 6 and Avatar rendering remain untested; CPU was not measured in this run.
- The expression-only left-outer-brow limitation remains. VRCFT 6's output setting has a different meaning from 5.4.5, and packages must match their exact upstream target.

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
