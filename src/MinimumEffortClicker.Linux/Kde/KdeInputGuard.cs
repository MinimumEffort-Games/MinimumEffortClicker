using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux.Kde;

/// <summary>
/// The only sender of input on KDE: a second copy of the app's own executable, started with
/// <see cref="Argument"/>, holding KWin's fake-input connection. The app sends one line per input over the pipe and
/// waits for "ok". When the pipe closes, because the app quit or crashed, the guard releases every key and button it
/// holds (KWin itself only releases keys) and removes the app's global shortcuts, then exits. While the screen is
/// locked it refuses presses and moves, so nothing reaches the lock screen.
/// </summary>
public static class KdeInputGuard
{
    public const string Argument = "--kde-input-guard";
    internal const string Ready = "ready", Denied = "denied", Ok = "ok", Locked = "locked";

    /// <summary>Runs the guard when the process was started for it; null otherwise. Call first in Main.</summary>
    public static int? RunIfRequested(string[] args) => args is [Argument] ? Run(Console.In, Console.Out) : null;

    private static int Run(TextReader commands, TextWriter replies)
    {
        FakeInput? input;
        try { input = FakeInput.Connect("Plays back the clicks and keys you recorded or set up."); }
        catch (Exception ex) when (ex is IOException or DllNotFoundException or EntryPointNotFoundException)
        { replies.WriteLine("error " + ex.Message); replies.Flush(); return 2; }
        if (input is null) { replies.WriteLine(Denied); replies.Flush(); return 3; }
        using var screen = new LockWatch();
        var buttons = new HashSet<uint>();
        var keys = new HashSet<int>();
        replies.WriteLine(Ready); replies.Flush();
        try
        {
            while (commands.ReadLine() is { } line)
            {
                string reply;
                try { reply = Handle(input, line, buttons, keys, screen.Locked); }
                catch (FormatException) { reply = "error Unknown input command."; }
                replies.WriteLine(reply); replies.Flush();
            }
        }
        catch (IOException) { /* The app is gone. */ }
        finally
        {
            try { ReleaseAll(input, buttons, keys); } catch (IOException) { }
            input.Dispose();
            KdeShortcuts.RemoveAll();
        }
        return 0;
    }

    /// <summary>Lines: "m x y" move, "b code 0|1" button, "k evdev 0|1" key, "s axis value" scroll, "r" release all.</summary>
    internal static string Handle(FakeInput input, string line, HashSet<uint> buttons, HashSet<int> keys, bool locked)
    {
        var parts = line.Split(' ');
        bool press = parts.Length == 3 && parts[2] == "1";
        if (locked && parts[0] is "m" or "s" || locked && press) return Locked;
        switch (parts[0])
        {
            case "m": input.MoveTo(double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)); break;
            case "b":
                uint button = uint.Parse(parts[1]);
                input.SetButton(button, press);
                if (press) buttons.Add(button); else buttons.Remove(button);
                break;
            case "k":
                int key = int.Parse(parts[1]);
                input.SetKey(key, press);
                if (press) keys.Add(key); else keys.Remove(key);
                break;
            case "s": input.Scroll(uint.Parse(parts[1]), double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)); break;
            case "r": ReleaseAll(input, buttons, keys); break;
            default: throw new FormatException();
        }
        input.Roundtrip();
        return Ok;
    }

    private static void ReleaseAll(FakeInput input, HashSet<uint> buttons, HashSet<int> keys)
    {
        foreach (int key in keys) input.SetKey(key, false);
        foreach (uint button in buttons) input.SetButton(button, false);
        keys.Clear(); buttons.Clear();
        input.Roundtrip();
    }

    /// <summary>Follows org.freedesktop.ScreenSaver, which KWin serves on Plasma. Unknown counts as unlocked.</summary>
    private sealed class LockWatch : IDisposable
    {
        private readonly DBusConnection? connection;
        private volatile bool locked;
        public bool Locked => locked;

        public LockWatch()
        {
            if (DBusAddress.Session is not { } address) return;
            try
            {
                connection = new DBusConnection(address);
                if (!Task.Run(() => connection.ConnectAsync().AsTask()).Wait(TimeSpan.FromSeconds(2))) return;
                Task.Run(() => connection.WatchSignalAsync(null, null, "org.freedesktop.ScreenSaver", "ActiveChanged",
                    (m, _) => m.GetBodyReader().ReadBool(), (Notification<bool> n) => { if (n.HasValue) locked = n.Value; },
                    ObserverFlags.None, false, null).AsTask()).Wait(TimeSpan.FromSeconds(2));
                locked = KdeHelper.ScreenLocked(connection) ?? false;
            }
            catch (Exception ex) when (ex is DBusExceptionBase or AggregateException or InvalidOperationException) { }
        }

        public void Dispose() => connection?.Dispose();
    }
}

/// <summary>The app's side of <see cref="KdeInputGuard"/>: starts it on first use and again if it ever dies.</summary>
internal sealed class KdeInput : IDisposable
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private readonly HashSet<uint> heldButtons = [];
    private Process? guard;
    private bool disposed;

    /// <summary>Starts the guard and checks KWin offers it fake input.</summary>
    public void Start() { lock (gate) EnsureGuard(); }

    public void Send(string line)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var process = EnsureGuard();
            string reply;
            try
            {
                process.StandardInput.WriteLine(line);
                reply = ReadReply(process);
            }
            catch (IOException ex) { Stop(); throw new HelperException("The KDE input helper stopped: " + ex.Message, ex); }
            if (reply == KdeInputGuard.Locked) throw new HelperException("The screen is locked, so no input is sent.");
            if (reply != KdeInputGuard.Ok) throw new HelperException(reply.StartsWith("error ", StringComparison.Ordinal) ? reply[6..] : "The KDE input helper refused the input.");
            var parts = line.Split(' ');
            if (parts[0] == "b") { if (parts[2] == "1") heldButtons.Add(uint.Parse(parts[1])); else heldButtons.Remove(uint.Parse(parts[1])); }
            else if (parts[0] == "r") heldButtons.Clear();
        }
    }

    private Process EnsureGuard()
    {
        if (guard is { HasExited: false } running) return running;
        bool restarted = guard is not null;
        Stop();
        string executable = Environment.ProcessPath ?? throw new HelperException("Cannot find this program's path.");
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
            throw new HelperException("Start the clicker from its own executable (not 'dotnet …dll') so KDE can recognise it.") { AccessDenied = true };
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(KdeInputGuard.Argument);
        var process = Process.Start(start) ?? throw new HelperException("Could not start the KDE input helper.");
        guard = process;
        string first;
        try { first = ReadReply(process); }
        catch (IOException ex) { Stop(); throw new HelperException("The KDE input helper did not start: " + ex.Message, ex); }
        if (first == KdeInputGuard.Denied)
        {
            Stop();
            throw new HelperException("KDE does not allow this copy of the app to send input. Choose Allow this copy of the app in Settings.") { AccessDenied = true };
        }
        if (first != KdeInputGuard.Ready) { Stop(); throw new HelperException(first.StartsWith("error ", StringComparison.Ordinal) ? first[6..] : "The KDE input helper did not start."); }
        // A guard that died left KWin holding our mouse buttons; a new connection can still let them go.
        if (restarted)
            foreach (uint button in heldButtons.ToArray())
            {
                process.StandardInput.WriteLine($"b {button} 0");
                ReadReply(process);
                heldButtons.Remove(button);
            }
        return process;
    }

    private static string ReadReply(Process process)
    {
        var read = process.StandardOutput.ReadLineAsync();
        if (!read.Wait(ReplyTimeout)) throw new IOException("no answer in time");
        return read.Result ?? throw new IOException("it closed");
    }

    private void Stop()
    {
        if (guard is null) return;
        try { guard.StandardInput.Close(); } catch (IOException) { }
        if (!guard.WaitForExit(1000)) { try { guard.Kill(); } catch (InvalidOperationException) { } }
        guard.Dispose();
        guard = null;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Stop(); // Closing the pipe makes the guard release everything.
        }
    }
}
