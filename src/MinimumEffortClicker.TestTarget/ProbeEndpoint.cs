using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace MinimumEffortClicker.TestTarget;

/// <summary>Opt-in, current-user-only local probe endpoint. No arbitrary commands or disk writes.</summary>
internal sealed class ProbeEndpoint : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    public ProbeEndpoint(MainWindow window, string name)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cancellation.Token);
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var command = await reader.ReadLineAsync(cancellation.Token);
                    if (command is not ("state" or "focus" or "minimize" or "close" or "capture-blue" or "capture-red")) { await writer.WriteLineAsync("{}"); continue; }
                    if (command == "close")
                    {
                        await writer.WriteLineAsync("{}");
                        _ = window.Dispatcher.BeginInvoke(window.Close);
                        return;
                    }
                    var state = await window.Dispatcher.InvokeAsync(() =>
                    {
                        if (command == "focus") window.FocusProbeArea();
                        if (command == "minimize") window.WindowState = System.Windows.WindowState.Minimized;
                        if (command is "capture-blue" or "capture-red") window.SetCapturePatch(command == "capture-red");
                        return JsonSerializer.Serialize(window.ProbeSnapshot());
                    });
                    await writer.WriteLineAsync(state);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });
    }
    public void Dispose() => cancellation.Cancel();
}
