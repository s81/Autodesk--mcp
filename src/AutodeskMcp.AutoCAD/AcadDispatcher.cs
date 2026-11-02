using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutodeskMcp.Bridge;

namespace AutodeskMcp.AutoCAD;

/// <summary>
/// Marshals work onto AutoCAD's main thread by posting to a hidden window created on that thread.
/// Handlers run in application context; they lock the document themselves before writing.
/// Work that has not started by the timeout is cancelled and never runs.
/// </summary>
internal sealed class AcadDispatcher : IMainThreadDispatcher
{
    private readonly Control _control;

    public AcadDispatcher()
    {
        _control = new Control();
        _control.CreateControl();
        _ = _control.Handle; // force handle creation on the current (main) thread
    }

    public Task<JsonNode?> InvokeAsync(Func<JsonNode?> work, TimeSpan timeout, CancellationToken ct)
    {
        var item = new PendingWork(work);
        _control.BeginInvoke(new Action(() => item.TryRun()));
        return item.WaitAsync(timeout, ct,
            $"AutoCAD's main thread did not respond within {timeout.TotalSeconds:0}s (modal dialog or long operation?).");
    }
}
