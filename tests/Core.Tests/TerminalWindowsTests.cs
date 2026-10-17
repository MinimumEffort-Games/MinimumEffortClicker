using MinimumEffortClicker.Core;

namespace Core.Tests;

[TestClass]
public sealed class TerminalWindowsTests
{
    [TestMethod]
    public void TerminalsOnWindowsAndLinuxAreRecognised()
    {
        // Windows: by program (with or without .exe) or by window class.
        Assert.IsTrue(TerminalWindows.Is("cmd.exe"));
        Assert.IsTrue(TerminalWindows.Is("PowerShell"));
        Assert.IsTrue(TerminalWindows.Is("WindowsTerminal"));
        Assert.IsTrue(TerminalWindows.Is("SomeHost", "ConsoleWindowClass"));
        Assert.IsTrue(TerminalWindows.Is(null, "CASCADIA_HOSTING_WINDOW_CLASS"));
        // Linux: by program or desktop app id.
        Assert.IsTrue(TerminalWindows.Is("gnome-terminal-server"));
        Assert.IsTrue(TerminalWindows.Is("org.kde.konsole.desktop"));
        Assert.IsTrue(TerminalWindows.Is("/usr/bin/kitty"));
        // Games and ordinary apps are not.
        foreach (var other in new[] { "notepad.exe", "Minecraft", "steam", "firefox", "", null })
            Assert.IsFalse(TerminalWindows.Is(other, "Chrome_WidgetWin_1"), other ?? "null");
    }
}
