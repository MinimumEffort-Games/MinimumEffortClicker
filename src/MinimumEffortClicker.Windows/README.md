# Windows boundary

M0 now includes narrowly scoped probe adapters for left mouse transitions, target checks,
F9 on a dedicated message thread and native overlay window styles. Only the isolated WindowsProbe
tool calls them. They are not a finished production backend: keyboard semantics, physical-input
collision tracking, target lifecycle, session lock/sleep and complete ownership policies remain M1 work.
The normal app shell has no automation controls. Default tests send no OS input.
