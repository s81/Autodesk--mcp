using System;
using System.Collections.Concurrent;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif
using System.Threading;
using System.Threading.Tasks;
using AutodeskMcp.Bridge;
using Autodesk.Revit.UI;

namespace AutodeskMcp.Revit;

/// <summary>
/// Runs work on Revit's API thread via an ExternalEvent. Revit only processes the event when it is idle
/// (no command running, no modal dialog), so callers may wait; work that has not started by the timeout is
/// cancelled and never runs.
/// </summary>
internal sealed class RevitDispatcher : IExternalEventHandler, IMainThreadDispatcher
{
    private readonly ConcurrentQueue<PendingWork> _queue = new();
    private readonly ExternalEvent _event;

    /// <summary>The UIApplication for the request currently executing; valid only on the API thread inside Execute.</summary>
    [ThreadStatic] private static UIApplication? _current;

    public RevitDispatcher()
    {
        _event = ExternalEvent.Create(this);
    }

    public static UIApplication Current =>
        _current ?? throw new InvalidOperationException("Revit API accessed outside the API thread.");

    public Task<JsonNode?> InvokeAsync(Func<JsonNode?> work, TimeSpan timeout, CancellationToken ct)
    {
        var item = new PendingWork(work);
        _queue.Enqueue(item);

        var raise = _event.Raise();
        if (raise is not (ExternalEventRequest.Accepted or ExternalEventRequest.Pending) && item.TryCancel())
            throw new BridgeException($"Revit refused the request ({raise}).");

        return item.WaitAsync(timeout, ct,
            $"Revit did not become idle within {timeout.TotalSeconds:0}s (a command, edit mode or modal dialog is probably active).");
    }

    public void Execute(UIApplication app)
    {
        _current = app;
        try
        {
            while (_queue.TryDequeue(out var item)) item.TryRun();
        }
        finally
        {
            _current = null;
        }
    }

    public string GetName() => "Autodesk MCP Bridge";
}
