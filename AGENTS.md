# Project instructions

Read README.md, CONTRIBUTING.md and docs/RELEASE_STATUS.md before changes.
Windows 11 x64, C#/.NET 10 and WPF. Core/Engine stay independent of WPF/native adapters.
Default tests use fake inputs/time/targets; never synthesize desktop input in CI.
Native probes are opt-in in an isolated interactive VM with a stop route.
Keep runtime offline and stopped on startup; imports/focus restoration never start it.
Use scripts/Build.ps1 and scripts/Test.ps1. Maintain pins, lock files and notices.
No drivers, injection, automatic elevation, background-game automation or services.
App-owned notices use AppDialog; keep Windows pickers native. Record unverified evidence.
Keep delivered builds/reports/test assets under Desktop/Clicker. Preserve existing data.
