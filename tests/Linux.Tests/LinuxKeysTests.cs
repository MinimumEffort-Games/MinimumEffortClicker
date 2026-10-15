using MinimumEffortClicker.Core;
using MinimumEffortClicker.Linux;

namespace Linux.Tests;

[TestClass]
public sealed class LinuxKeysTests
{
    [TestMethod]
    public void EverySequenceKeyHasAnEvdevCodeThatMapsBack()
    {
        foreach (var (name, scan) in SequenceKeys.Supported)
        {
            int evdev = LinuxKeys.EvdevFromScanCode(scan);
            Assert.AreNotEqual(0, evdev, name);
            Assert.AreEqual(scan, LinuxKeys.ScanCodeFromEvdev(evdev), name);
        }
    }

    [TestMethod]
    public void CommonKeysUseTheLinuxEvdevNumbers()
    {
        Assert.AreEqual(17, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["W"]));
        Assert.AreEqual(66, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["F8"]));
        Assert.AreEqual(103, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["Up"]));
        Assert.AreEqual(97, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["Right Ctrl"]));
        Assert.AreEqual(99, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["Print Screen"]));
        Assert.AreEqual(69, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["Num Lock"]));
        Assert.AreEqual(183, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["F13"]));
        Assert.AreEqual(194, LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["F24"]));
    }

    [TestMethod]
    public void EverySupportedShortcutMapsToAnEvdevCodeAndBack()
    {
        foreach (int key in Enumerable.Range(1, 0xFE).Where(ShortcutKeys.Supported))
        {
            if (key is 12 or 0xDF) continue; // Clear shares keypad 5; OEM 8 has no fixed Linux position.
            int evdev = LinuxKeys.EvdevFromVirtualKey(key);
            Assert.AreNotEqual(0, evdev, ShortcutKeys.Label(key));
            Assert.AreEqual(key, LinuxKeys.VirtualKeyFromEvdev(evdev), ShortcutKeys.Label(key));
        }
    }

    [TestMethod]
    public void DefaultControlsAreFunctionKeys()
    {
        Assert.AreEqual(66, LinuxKeys.EvdevFromVirtualKey(0x77)); // F8 start
        Assert.AreEqual(67, LinuxKeys.EvdevFromVirtualKey(0x78)); // F9 stop all
        Assert.AreEqual(68, LinuxKeys.EvdevFromVirtualKey(0x79)); // F10 record
        Assert.AreEqual("F9", LinuxKeys.Accelerator(0x78));
        Assert.AreEqual("w", LinuxKeys.Accelerator('W'));
        Assert.IsNull(LinuxKeys.Accelerator(1)); // Mouse buttons cannot be GNOME accelerators.
    }

    [TestMethod]
    public void MouseButtonsMapBothWays()
    {
        foreach (var button in Enum.GetValues<PointerButton>())
        {
            Assert.AreEqual(button, LinuxKeys.ButtonFromEvdev(LinuxKeys.EvdevButton(button)));
            Assert.AreEqual(LinuxKeys.EvdevButton(button), LinuxKeys.EvdevFromVirtualKey(ShortcutKeys.MouseKey(button)));
        }
        Assert.AreEqual(1u, LinuxKeys.ClutterButton(PointerButton.Left));
        Assert.AreEqual(3u, LinuxKeys.ClutterButton(PointerButton.Right));
        Assert.AreEqual(8u, LinuxKeys.ClutterButton(PointerButton.XButton1));
    }

    [TestMethod]
    public void ModifiersAreRecognised()
    {
        foreach (string name in new[] { "Left Ctrl", "Right Ctrl", "Left Shift", "Right Shift", "Left Alt", "Right Alt" })
            Assert.IsTrue(LinuxKeys.IsModifier(LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported[name])), name);
        Assert.IsFalse(LinuxKeys.IsModifier(LinuxKeys.EvdevFromScanCode(SequenceKeys.Supported["W"])));
    }

    [TestMethod]
    public void HeldInputsResolveFromShortcutKeys()
    {
        Assert.AreEqual(InputControl.Key(SequenceKeys.Supported["W"]), DesktopRuntime.InputFromVirtualKey('W'));
        Assert.AreEqual(InputControl.Mouse(PointerButton.Right), DesktopRuntime.InputFromVirtualKey(2));
        Assert.IsTrue(DesktopRuntime.InputMatchesKey(InputControl.Key(SequenceKeys.Supported["W"]), 'W'));
        Assert.IsFalse(DesktopRuntime.InputMatchesKey(InputControl.Key(SequenceKeys.Supported["W"]), 'Q'));
    }

    [TestMethod]
    public void CancellingAPausedRunKeepsTheStopReason()
    {
        // The engine unpauses from its cleanup when a paused run is stopped; the stop reason must reach the log.
        Assert.IsNull(DesktopRuntime.PauseStatus(paused: false, automatic: true, cancelling: true, "Stopped by you."));
        Assert.AreEqual(("Running", "Resumed explicitly after target and physical-input checks."),
            DesktopRuntime.PauseStatus(paused: false, automatic: false, cancelling: false, "Resuming the remaining timing after explicit review."));
        Assert.AreEqual(("Running", "The target window is back in front. Continuing."),
            DesktopRuntime.PauseStatus(paused: false, automatic: true, cancelling: false, "The target window is back in front. Continuing."));
        Assert.AreEqual("Paused", DesktopRuntime.PauseStatus(paused: true, automatic: true, cancelling: true, "Stopped by you.")?.State);
    }
}
