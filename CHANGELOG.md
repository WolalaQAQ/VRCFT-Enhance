# Changelog

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
