# Project instructions

Read docs/Handoff/Where-We-Are.md first: current state, the open list and what to test.
Then README.md, CONTRIBUTING.md and docs/RELEASE_STATUS.md.

Windows 11 x64, C#/.NET 10 and WPF. Core/Engine stay independent of WPF/native adapters.
The four pages (Actions, Toggles, Screenshot, Settings) are the whole product; code that no page can reach is deleted, not kept.
Linux preview: Avalonia app + MinimumEffortClicker.Linux adapters, built from MinimumEffortClicker.Linux.slnx
with scripts/linux/*.sh. Keep the Windows solution, lock files and behaviour unchanged by Linux work.
Linux native probes run only in the private headless GNOME Shell (scripts/linux/run-headless-probe.sh).
Default tests use fake inputs/time/targets; never synthesize desktop input in CI.
Native probes are opt-in inside an isolated virtual machine (MEC_ISOLATED_VM=1, standard user, F9 stop route).
Keep runtime offline and stopped on startup; loading a macro or restored focus never starts it. A running window-target run that pauses because its window left the front resumes by itself once the window is back in front for half a second (User, 2026-10-13); a pause the user asked for stays paused.
Use scripts/Build.ps1 and scripts/Test.ps1; scripts/Build-Public.ps1 writes the ZIP under artifacts/package.
Maintain pins, lock files and licence notices. Saved data migrates forward and is never reset.
No drivers, injection, automatic elevation, background-game automation or services.
App-owned notices use AppDialog; keep Windows pickers native. Record unverified evidence honestly.
End every session by updating docs/Handoff/Where-We-Are.md.
This repository is public: in its files, commits and pull requests the owner is called User, never by name, and no personal data (e-mail, home folders, machine names, accounts) goes in.
