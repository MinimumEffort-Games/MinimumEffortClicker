using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MinimumEffortClicker.Engine;

namespace MinimumEffortClicker.Windows;

/// <summary>One scheduler-owned timer. No busy spin, timeBeginPeriod or global timer setting.</summary>
public sealed class WindowsDeadlineWait : IWaitStrategy, IDisposable
{
    private sealed class TimerSignal : WaitHandle
    {
        public TimerSignal(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
    private readonly IClock clock;
    private readonly TimerSignal timer;
    public WindowsDeadlineWait(IClock clock)
    {
        this.clock = clock;
        var handle = CreateWaitableTimerExW(0, null, 2, 0x00100002); // high resolution; synchronize + modify
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "Scheduler timer could not be created."); }
        timer = new(handle);
    }
    public void WaitUntil(TimeSpan deadline, CancellationToken stop) => Wait(deadline, stop, null);
    public bool WaitUntilOrChanged(TimeSpan deadline, CancellationToken stop, CancellationToken changed) => Wait(deadline, stop, changed);
    private bool Wait(TimeSpan deadline, CancellationToken stop, CancellationToken? changed)
    {
        WaitHandle[] signals = changed is { } update ? [stop.WaitHandle, update.WaitHandle, timer] : [stop.WaitHandle, timer];
        try
        {
            while (true)
            {
                stop.ThrowIfCancellationRequested();
                if (changed?.IsCancellationRequested == true) return false;
                var remaining = deadline - clock.Elapsed;
                if (remaining <= TimeSpan.Zero) return true;
                long due = -remaining.Ticks; // relative 100 ns units; no wall-clock conversion
                if (!SetWaitableTimerEx(timer.SafeWaitHandle, ref due, 0, 0, 0, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                WaitHandle.WaitAny(signals);
            }
        }
        finally { CancelWaitableTimer(timer.SafeWaitHandle); }
    }
    public void Dispose() => timer.Dispose();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimerEx(SafeWaitHandle timer, ref long due, int period, nint callback, nint argument, nint wakeContext, uint tolerableDelay);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelWaitableTimer(SafeWaitHandle timer);
}
