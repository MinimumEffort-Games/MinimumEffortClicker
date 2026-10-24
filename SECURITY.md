# Security

Minimum Effort Clicker sends synthetic keyboard and mouse input on your own PC, on Windows and on Linux. It runs as a
normal user and installs no driver or service. It never asks for administrator rights by itself: the only exception
is the Linux **Turn on / Turn off input access** button, which asks for your password in the system's own window
(pkexec) to add or remove one input-access rule, and only when you press it.

The portable builds make one network connection, and only when you press Send report in Bug report: the report you
see goes to minimumeffort.games, which e-mails it to the developer. The Steam build also goes online through the Steam client: when you
share a macro on the Workshop, and when Steam downloads macros you subscribed to. Settings and macros stay in the
`Data` folder beside the app (portable build) or in your local application data.

## Reporting a problem

If you find a way the app could hold input down after it is told to stop, act outside the selected target, read
or write files it should not, or be tricked by a crafted macro file, please report it privately rather than in a
public issue: open a GitHub security advisory on this repository (Security tab, "Report a vulnerability"), or
email the address on the Minimum Effort website. Include the app version, your system (Windows version, or Linux
distribution and desktop) and the steps. You will get an acknowledgement within a week.

## What the releases guarantee

Releases are built by the GitHub Actions workflow in this repository from a tagged commit: a Windows ZIP and a Linux
`tar.gz`. Each carries `build-manifest.json` with the source commit and a SHA-256 for every file, and the archive hash
is published next to it. The releases are not code-signed, so Windows SmartScreen may warn; that warning is
expected and is not a reason to turn protections off. Verify the hash instead.

## Scope notes

- Some games and applications forbid macros. Follow their rules; this project cannot promise account safety.
- The probe tools under `tools/WindowsProbe` and `tools/LinuxProbe` send real input. They only run inside an isolated
  virtual machine or a private headless desktop session, never on a normal desktop or in ordinary CI.
