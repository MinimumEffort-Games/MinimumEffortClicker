# Security

Minimum Effort Clicker sends synthetic keyboard and mouse input on your own PC. It runs as a normal user, never
asks for elevation, installs no driver or service, and makes no network connections. Settings and macros stay in
the `Data` folder beside the app (portable build) or in your local application data.

## Reporting a problem

If you find a way the app could hold input down after it is told to stop, act outside the selected target, read
or write files it should not, or be tricked by a crafted macro file, please report it privately rather than in a
public issue: open a GitHub security advisory on this repository (Security tab, "Report a vulnerability"), or
email the address on the Minimum Effort website. Include the app version, your Windows version and the steps.
You will get an acknowledgement within a week.

## What the releases guarantee

Release ZIPs are built by the GitHub Actions workflow in this repository from a tagged commit. Each ZIP carries
`build-manifest.json` with the source commit and a SHA-256 for every file, and the archive hash is published next
to it. The preview releases are not code-signed, so Windows SmartScreen may warn; that warning is expected and is
not a reason to turn protections off. Verify the hash instead.

## Scope notes

- Some games and applications forbid macros. Follow their rules; this project cannot promise account safety.
- The VM probe tool under `tools/WindowsProbe` sends real input and only runs inside an isolated virtual machine.
