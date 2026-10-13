# Release status

Version: **0.5.0-preview.34.16**, portable Windows 11 x64, macro file schema **16**
(the published preview ZIP still writes schema 15; the next release upgrades it on load).
The owner authorized public source under MIT and a free unsigned preview on
13 October 2026. Signing is a later step. No paid provider, certificate,
installer, Store/Steam submission, or application runtime service was purchased
or configured.

## Verification

The R34.16 private baseline recorded 297 fake-only passing tests and bounded
Windows regression, recovery, capture and spam checks in the isolated VM. After
the October 2026 cleanup the suite is 245 tests; the removed tests covered
features that no longer exist.
The public snapshot must independently pass `scripts/Test.ps1`, locked restore
and Release build. Its release manifest records the actual source commit,
runtime and per-file hashes. Do not infer full hardware acceptance from these
checks. See the accompanying local release report for the public build's results.

Remaining acceptance: eight-hour full WPF application endurance; controlled
physical recording/overlap; additional monitor/DPI, suspend/session and
accessibility scenarios. A previous VM freeze's cause is unresolved. No real OS
shutdown was executed as a test. Native playback tests are not run in CI.

## Signing

This preview is unsigned. No self-signed certificate is presented as publicly
trusted, and checksums are not signatures. Microsoft explains the trusted
provider requirement for Smart App Control:
https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control

SignPath Foundation offers free signing for eligible open-source projects,
subject to their review and terms. Eligibility/approval is not established for
this project. A future application should use the public repository and build
provenance; it is not part of this release:
https://signpath.org/ and https://signpath.org/terms.html

If trusted signing is introduced, sign app assemblies/executables through the
approved provider, timestamp and verify their signatures, then regenerate the
manifest and ZIP checksums. Keep all signing credentials outside the repository.

## Source publication

The public repository begins with a clean snapshot, preserving the full private
development repository locally. It excludes owner Data, old history/evidence,
VM handoff receipts, host credential helpers, unlicensed historical pixel assets,
download caches and generated binaries. Required approved brand assets and
dependency notices are included. No owner data is copied into the portable ZIP.
