using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AutodeskMcp.Bridge;

/// <summary>
/// One unit of work queued for a host's main thread. Guarantees that work which has not started before the
/// caller gives up is never executed later (so a timed-out request cannot silently modify the model).
/// </summary>
public sealed class PendingWork
{
    private const int Queued = 0, Running = 1, Cancelled = 2;
    private readonly Func<JsonNode?> _work;
    private readonly TaskCompletionSource<JsonNode?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;

    public PendingWork(Func<JsonNode?> work) => _work = work;

    /// <summary>Called on the main thread. Returns false if the work was cancelled and therefore skipped.</summary>
    public bool TryRun()
    {
        if (Interlocked.CompareExchange(ref _state, Running, Queued) != Queued) return false;
        try { _completion.SetResult(_work()); }
        catch (Exception ex) { _completion.SetException(ex); }
        return true;
    }

    /// <summary>Cancels the work if it has not started yet.</summary>
    public bool TryCancel() => Interlocked.CompareExchange(ref _state, Cancelled, Queued) == Queued;

    /// <summary>
    /// Waits for the result. If the work has not *started* within <paramref name="timeout"/>, it is cancelled
    /// and a <see cref="BridgeException"/> with <paramref name="busyMessage"/> is thrown. Work that already
    /// started is always awaited to completion.
    /// </summary>
    public async Task<JsonNode?> WaitAsync(TimeSpan timeout, CancellationToken ct, string busyMessage)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, delayCts.Token);
        var first = await Task.WhenAny(_completion.Task, delay).ConfigureAwait(false);
        delayCts.Cancel();

        if (first != _completion.Task && TryCancel())
        {
            ct.ThrowIfCancellationRequested();
            throw new BridgeException(busyMessage + " The request was cancelled and not executed.");
        }
        return await _completion.Task.ConfigureAwait(false);
    }
}
