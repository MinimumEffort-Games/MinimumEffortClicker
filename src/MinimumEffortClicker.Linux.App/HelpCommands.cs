using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MinimumEffortClicker.Linux;
using System.Diagnostics;
using System.IO;
using System.Text;
using MinimumEffortClicker.Core;

namespace MinimumEffortClicker.App;

public partial class MainWindow
{
    private readonly Queue<string> diagnosticStates = new();
    private string? lastDiagnosticState;
    private void RecordDiagnosticState(RuntimeSnapshot snapshot)
    {
        if (snapshot.State == lastDiagnosticState) return;
        lastDiagnosticState = snapshot.State;
        if (diagnosticStates.Count == 64) diagnosticStates.Dequeue();
        // Only application-defined states and counts: no keys, names, titles, paths or images.
        diagnosticStates.Enqueue($"{DateTimeOffset.UtcNow:O}  {snapshot.State}; inputs={snapshot.InputsSent}; loops={snapshot.CompletedCycles}; cleanup={snapshot.CleanupPending}");
    }
}
