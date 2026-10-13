# Contributing

Use Windows 11 x64 and the exact SDK/package pins. Run `scripts/Build.ps1` and
`scripts/Test.ps1`; default tests must never synthesize desktop input. Keep
Core/Engine independent of WPF/native adapters. Preserve stopped startup,
explicit start, foreground validation, independent Stop All and owned-input
cleanup. Loading/importing or restored focus must not begin playback.

Real-input probes require explicit opt-in, an isolated interactive VM and a
working stop route. Never run them on a user's normal desktop or ordinary CI.
Avoid new runtime services, drivers, elevation, injection and background-game
automation. App-owned notices use AppDialog; Windows file pickers remain native.

Describe the concrete change and validation in your PR. Document missing exit
evidence honestly. Do not include local Data, screenshots/recordings from people,
credentials, generated build output, or assets without redistribution rights.
Dependencies and runtime changes need lock-file and licence-inventory review.
