using System.Diagnostics;
using System.Text.Json;
using MinimumEffortClicker.Core;
using MinimumEffortClicker.Engine;

// No Windows adapter, hook, input API or capture dependency. All output is counted in memory.
if (args.Length != 2 || !int.TryParse(args[0], out int seconds) || seconds is < 6 or > 28800)
{ Console.Error.WriteLine("EngineSoak <seconds: 6..28800> <new-report-directory>"); return 2; }
if (Directory.Exists(args[1])) throw new IOException("Use a fresh report directory to preserve earlier evidence.");
Directory.CreateDirectory(args[1]);
var options = new JsonSerializerOptions { WriteIndented = false };
using var log = new StreamWriter(Path.Combine(args[1], "samples.jsonl")) { AutoFlush = true };
var clock = new StopwatchClock();
var sink = new CountingSink(clock);
var target = new FakeTarget();
var executor = new ClickExecutor(sink, clock, new DeadlineWait(clock), target) { ClickRateLimit = new(100) };
using var process = Process.GetCurrentProcess();
var start = DateTimeOffset.UtcNow;
var limit = TimeSpan.FromSeconds(seconds);
int phase = 0;
long totalTransitions = 0;
double maxPrivate = 0;
int maxHandles = 0;
string? error = null;
try
{
    while (clock.Elapsed < limit)
    {
        var duration = TimeSpan.FromSeconds(Math.Min(60, (limit - clock.Elapsed).TotalSeconds));
        if (duration <= TimeSpan.Zero) break;
        int mode = phase++ % 6;
        int cps = mode < 4 ? new[] { 1, 10, 20, 100 }[mode] : 0;
        string scenario = cps > 0 ? $"repeat-{cps}" : mode == 4 ? "hold" : "ordered-wait";
        sink.Reset();
        var started = clock.Elapsed; var cpu = process.TotalProcessorTime;
        using var stop = new CancellationTokenSource(duration);
        ClickRunResult result;
        if (mode == 5)
            result = executor.Execute(SequenceCompiler.Compile(new([SequenceStep.Wait(TimeSpan.FromDays(1))])), target.Id, stop.Token);
        else
        {
            var helper = cps > 0
                ? new HelperPlan(InputControl.Mouse(PointerButton.Left), HelperMode.Repeat, TimeSpan.FromSeconds(1d / cps), TimeSpan.FromMilliseconds(2), 0)
                : new HelperPlan(InputControl.Key(0x11), HelperMode.Hold, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(2), 0);
            result = executor.Execute(helper, target.Id, stop.Token);
        }
        process.Refresh();
        var elapsed = clock.Elapsed - started;
        double privateMiB = process.PrivateMemorySize64 / 1048576d;
        maxPrivate = Math.Max(maxPrivate, privateMiB); maxHandles = Math.Max(maxHandles, process.HandleCount);
        totalTransitions += sink.Transitions;
        log.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, scenario, elapsedSeconds = elapsed.TotalSeconds,
            cpuPercentOneCore = (process.TotalProcessorTime - cpu).TotalSeconds / elapsed.TotalSeconds * 100,
            privateMiB, handles = process.HandleCount, managedMiB = GC.GetTotalMemory(false) / 1048576d,
            allocatedMiB = GC.GetTotalAllocatedBytes() / 1048576d, transitions = sink.Transitions,
            maxIntervalMs = sink.MaxInterval, meanIntervalMs = sink.IntervalCount == 0 ? 0 : sink.IntervalTotal / sink.IntervalCount,
            requestedIntervalMs = cps == 0 ? 0 : 1000d / cps, cancellationOvershootMs = Math.Max(0, (elapsed - duration).TotalMilliseconds),
            outcome = result.Outcome.ToString(), result.CleanupSucceeded, owned = sink.HeldCount }, options));
        if (!result.CleanupSucceeded || executor.HasOwnedInput || sink.HeldCount != 0 || result.Outcome != ClickRunOutcome.Cancelled)
            throw new InvalidOperationException("Cancellation/ownership invariant failed.");
    }
}
catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
File.WriteAllText(Path.Combine(args[1], "result.json"), JsonSerializer.Serialize(new {
    scope = "Input-free real-clock engine soak; fake target and counting sink. Excludes WPF, native input, capture and physical stop latency.",
    startedUtc = start, finishedUtc = DateTimeOffset.UtcNow, requestedSeconds = seconds, elapsedSeconds = clock.Elapsed.TotalSeconds,
    passed = error is null, phases = phase, totalTransitions, maxPrivateMiB = maxPrivate, maxHandles,
    os = Environment.OSVersion.VersionString, runtime = Environment.Version.ToString(), logicalProcessors = Environment.ProcessorCount,
    error }, new JsonSerializerOptions { WriteIndented = true }));
return error is null ? 0 : 1;

sealed class FakeTarget : IForegroundTargetProvider
{
    public Guid Id { get; } = Guid.NewGuid();
    public ForegroundTarget? Current => new(Id, true);
}
sealed class CountingSink(IClock clock) : IInputSink
{
    private readonly HashSet<InputControl> held = [];
    private TimeSpan? lastDown;
    public int HeldCount => held.Count;
    public long Transitions { get; private set; }
    public long IntervalCount { get; private set; }
    public double IntervalTotal { get; private set; }
    public double MaxInterval { get; private set; }
    public void Reset() { Transitions = IntervalCount = 0; IntervalTotal = MaxInterval = 0; lastDown = null; }
    public bool TrySend(InputTransition input)
    {
        if (input.Edge == InputEdge.Down)
        {
            if (!held.Add(input.Control)) throw new InvalidOperationException("Duplicate owned down.");
            if (lastDown is { } previous)
            { double interval = (clock.Elapsed - previous).TotalMilliseconds; IntervalCount++; IntervalTotal += interval; MaxInterval = Math.Max(MaxInterval, interval); }
            lastDown = clock.Elapsed;
        }
        else if (!held.Remove(input.Control)) throw new InvalidOperationException("Unowned release.");
        Transitions++; return true;
    }
}
