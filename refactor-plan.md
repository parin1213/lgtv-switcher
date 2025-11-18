# Rx-based Display Sync Refactor Plan

- [x] Define new snapshot domain models (`DisplaySnapshot`, `MonitorInfo`, `SnapshotEqualityComparer`) in `LGTVSwitcher.Core`.
- [x] Introduce `ISnapshotStream` abstraction backed by `Subject<DisplaySnapshot>` in the Windows display detection layer and register it with DI.
- [x] Refactor `WindowsMonitorDetector` (or appropriate producer) to publish `DisplaySnapshot` instances into the subject whenever Win32 events occur.
- [x] Replace `DisplaySyncWorker` channel loop with an Rx pipeline that consumes the snapshot observable (debounce → filter → distinct → LG TV sync) and handles WebSocket errors gracefully.
- [x] Update DI wiring (Sandbox host and any other hosts) to register the snapshot stream and ensure `DisplaySyncWorker` receives the observable.
- [x] Run `dotnet build` and manually validate the pipeline with logging to ensure no regressions.
