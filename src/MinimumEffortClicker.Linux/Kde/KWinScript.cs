using System.Text.Json;
using System.Text.Json.Nodes;
using Tmds.DBus.Protocol;

namespace MinimumEffortClicker.Linux.Kde;

/// <summary>
/// Loads kwin-helper.js into KWin (org.kde.KWin /Scripting) and asks it questions. KWin scripts cannot receive D-Bus
/// calls, so the script long-polls this object: its "Next" call is held here until there is a question, and the next
/// "Next" carries the answer (about 1 ms per question). Only KWin's own connection may call these methods.
/// </summary>
internal sealed class KWinScript : IPathMethodHandler, IDisposable
{
    public const string ObjectPath = "/io/github/MinimumEffortGames/ClickerKWin";
    private const string Interface = "io.github.MinimumEffortGames.ClickerKWin1";
    private const string PluginName = "me-clicker-helper";
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(2);
    // KWin's call gives up after 25 s; hand it an idle answer before that so the poll never breaks.
    private static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(15);

    private readonly DBusConnection connection;
    private readonly object gate = new();
    private readonly Queue<string> questions = new();
    private readonly Dictionary<long, TaskCompletionSource<JsonNode?>> waiting = [];
    private readonly Action<string> onEvent;
    private readonly Timer idle;
    private MethodContext? parked;
    private string kwinOwner = "";
    private long sequence;
    private volatile bool disposed;

    public KWinScript(DBusConnection connection, Action<string> onEvent)
    {
        this.connection = connection;
        this.onEvent = onEvent;
        idle = new Timer(_ => Release(parkedOnly: true), null, IdleAfter, IdleAfter);
    }

    public string Path => ObjectPath;
    public bool HandlesChildPaths => false;

    /// <summary>Loads the script (replacing a copy a crashed clicker left) and waits until it is asking.</summary>
    public void Load()
    {
        kwinOwner = KdeHelper.Call(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "GetNameOwner", "s",
            (ref MessageWriter w) => w.WriteString("org.kde.KWin"), (m, _) => m.GetBodyReader().ReadString());
        connection.AddMethodHandler(this);
        string file = System.IO.Path.Combine(ScriptFolder(), $"me-clicker-kwin-{Environment.ProcessId}.js");
        File.WriteAllText(file, Source.Replace("%SERVICE%", connection.UniqueName, StringComparison.Ordinal));
        try
        {
            Unload();
            int id = KdeHelper.Call(connection, "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "loadScript", "ss",
                (ref MessageWriter w) => { w.WriteString(file); w.WriteString(PluginName); }, (m, _) => m.GetBodyReader().ReadInt32());
            if (id < 0) throw new HelperException("KWin did not load the clicker's window script.");
            KdeHelper.Call(connection, "org.kde.KWin", $"/Scripting/Script{id}", "org.kde.kwin.Script", "run", null, null, (_, _) => true);
            Ask("focus"); // Throws when the script never starts asking.
        }
        finally { File.Delete(file); } // KWin has read it by the time it answers.
    }

    /// <summary>
    /// Where KWin can read the script file. Inside Steam's sandbox the runtime folder is the sandbox's own, which KWin
    /// cannot open ("Could not open /run/user/1000/…", CachyOS 2026-10-23); the home folder is shared with the desktop.
    /// </summary>
    private static string ScriptFolder()
    {
        if (!LinuxSetup.InSteamSandbox && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime) return runtime;
        if (!LinuxSetup.InSteamSandbox) return System.IO.Path.GetTempPath();
        string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } configured ? configured
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        string folder = System.IO.Path.Combine(cache, "minimum-effort-clicker");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string Source { get; } = ReadSource();
    private static string ReadSource()
    {
        using var stream = typeof(KWinScript).Assembly.GetManifestResourceStream("MinimumEffortClicker.Linux.Kde.kwin-helper.js")
            ?? throw new InvalidOperationException("kwin-helper.js is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Asks the script one question and returns its answer ("r"), or throws when KWin does not answer.</summary>
    public JsonNode? Ask(string operation, Action<JsonObject>? arguments = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var question = new JsonObject { ["s"] = 0, ["o"] = operation };
        arguments?.Invoke(question);
        var answer = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            long id = ++sequence;
            question["s"] = id;
            waiting[id] = answer;
            questions.Enqueue(question.ToJsonString());
        }
        Release(parkedOnly: false);
        if (!answer.Task.Wait(AnswerTimeout))
        {
            lock (gate) waiting.Remove((long)question["s"]!);
            throw new HelperException("KWin's window script did not answer in time.");
        }
        return answer.Task.Result;
    }

    /// <summary>Sends the next question (or an idle answer) to a waiting "Next" call.</summary>
    private void Release(bool parkedOnly)
    {
        MethodContext? context;
        string text;
        lock (gate)
        {
            if (parked is null || parkedOnly && questions.Count > 0) return;
            if (parkedOnly && !disposed) text = "{\"o\":\"idle\"}";
            else if (questions.Count > 0) text = questions.Dequeue();
            else if (disposed) text = "{\"o\":\"quit\"}";
            else return;
            context = parked; parked = null;
        }
        Reply(context, text);
    }

    private static void Reply(MethodContext context, string text)
    {
        try
        {
            using var writer = context.CreateReplyWriter("s");
            writer.WriteString(text);
            context.Reply(writer.CreateMessage());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
        finally { context.Dispose(); }
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        if (request.SenderAsString != kwinOwner || request.InterfaceAsString != Interface) { context.ReplyUnknownMethodError(); return default; }
        string member = request.MemberAsString ?? "";
        string text = request.SignatureAsString == "s" ? request.GetBodyReader().ReadString() : "";
        if (member == "Event")
        {
            using (var writer = context.CreateReplyWriter(null)) context.Reply(writer.CreateMessage());
            onEvent(text);
            return default;
        }
        if (member != "Next") { context.ReplyUnknownMethodError(); return default; }
        if (text.Length > 0) Answer(text);
        context.DisposesAsynchronously = true;
        MethodContext? previous;
        lock (gate) { previous = parked; parked = context; }
        if (previous is not null) Reply(previous, "{\"o\":\"idle\"}");
        Release(parkedOnly: false);
        return default;
    }

    private void Answer(string text)
    {
        JsonNode? reply;
        try { reply = JsonNode.Parse(text); } catch (JsonException) { return; }
        if (reply?["s"]?.GetValue<long>() is not { } id) return;
        TaskCompletionSource<JsonNode?>? answer;
        lock (gate) { if (waiting.Remove(id, out answer)) { } }
        if (answer is null) return;
        if (reply["e"] is { } error) answer.TrySetException(new HelperException("KWin's window script failed: " + error.GetValue<string>()));
        else answer.TrySetResult(reply["r"]?.DeepClone());
    }

    private void Unload()
    {
        try
        {
            KdeHelper.Call(connection, "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting", "unloadScript", "s",
                (ref MessageWriter w) => w.WriteString(PluginName), (m, _) => m.GetBodyReader().ReadBool());
        }
        catch (HelperException) { /* Nothing loaded, or KWin is gone. */ }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        idle.Dispose();
        Release(parkedOnly: false); // Tells the script to stop asking.
        Unload();
        connection.RemoveMethodHandler(ObjectPath);
        lock (gate)
        {
            foreach (var answer in waiting.Values) answer.TrySetException(new ObjectDisposedException(nameof(KWinScript)));
            waiting.Clear();
        }
    }
}
