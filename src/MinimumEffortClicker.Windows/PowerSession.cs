using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MinimumEffortClicker.Windows;

/// <summary>Created and disposed on the scheduler thread; never changes the saved Windows power plan.</summary>
internal sealed class PowerSession : IDisposable
{
    private readonly bool enabled;
    private bool disposed;
    public bool Succeeded { get; }
    public PowerSession(bool keepAwake)
    {
        enabled = keepAwake;
        Succeeded = !enabled || SetThreadExecutionState(0x80000003) != 0;
    }
    public void Dispose() { if (!disposed && enabled && Succeeded) SetThreadExecutionState(0x80000000); disposed = true; }
    public void SetPaused(bool paused)
    {
        if (!disposed && enabled && Succeeded && SetThreadExecutionState(paused ? 0x80000000u : 0x80000003u) == 0)
            throw new InvalidOperationException("Windows rejected the playback power-state transition.");
    }
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}

public static class PcPower
{
    // Only called after an explicitly reviewed, in-memory schedule and a cancellable app countdown.
    // /t 0 avoids the implicit forced app termination associated with a positive OS timeout.
    public static void RequestShutdown()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/s", "/t", "0", "/d", "p:0:0" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not receive the shutdown request.");
    }
}
