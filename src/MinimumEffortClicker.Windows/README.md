# Windows

The native layer the WPF app runs on. Core and Engine never reference it.

- `DesktopRuntime`: one scheduler worker that runs the engine, plus target selection, foreground checks, pause and
  resume, cleanup retry and the status snapshot the UI reads.
- `DesktopControl`: one dedicated message thread for the registered F-key shortcuts, the low-level keyboard and
  mouse hooks, physical-input tracking and the window-foreground events.
- `DesktopNative`, `ProbeNative`: SendInput and the other P/Invoke calls. Every injected event carries a tag so the
  hooks never mistake the app's own input for the user's.
- `DesktopRecorder`, `SequenceConditions`, `DesktopScreenshot`, `PowerSession`, `WindowChromeTheme`,
  `WindowsDeadlineWait`, `BackgroundClicks`: recording, Wait Until conditions, screenshots, keep-awake, title-bar
  theming, the high-resolution scheduler timer and cursor-free clicks.

Default tests never load this project. The real-input probe in `tools/WindowsProbe` exercises it inside an isolated
virtual machine only.
