using System.Diagnostics;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif
using AutodeskMcp.Bridge;

namespace AutodeskMcp.Tests;

/// <summary>Runs work on a dedicated thread, standing in for Revit's/AutoCAD's main thread.</summary>
internal sealed class ThreadDispatcher : IMainThreadDispatcher, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<PendingWork> _queue = new();
    private readonly Thread _thread;
    public volatile bool Paused;
    public int MainThreadId { get; private set; }

    public ThreadDispatcher()
    {
        _thread = new Thread(() =>
        {
            MainThreadId = Environment.CurrentManagedThreadId;
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                while (Paused) Thread.Sleep(10);
                item.TryRun();
            }
        }) { IsBackground = true, Name = "fake-main-thread" };
        _thread.Start();
    }

    public Task<JsonNode?> InvokeAsync(Func<JsonNode?> work, TimeSpan timeout, CancellationToken ct)
    {
        var item = new PendingWork(work);
        _queue.Add(item, ct);
        return item.WaitAsync(timeout, ct, "Fake host busy.");
    }

    public void Dispose() => _queue.CompleteAdding();
}

/// <summary>A registered, listening fake host of the given product.</summary>
internal sealed class FakeHost : IDisposable
{
    public InstanceInfo Info { get; }
    public ThreadDispatcher Dispatcher { get; } = new();
    public int ModifyCalls;
    private readonly BridgeHost _host;

    public FakeHost(string product, BridgeConfig? config = null, IEnumerable<BridgeCommand>? extra = null)
    {
        using var p = Process.GetCurrentProcess();
        string unique = Guid.NewGuid().ToString("N").Substring(0, 8);
        Info = new InstanceInfo
        {
            InstanceId = $"{product}-2099-{p.Id}-{unique}",
            Product = product,
            ProductVersion = "2099",
            ProductBuild = "fake",
            ProcessId = p.Id,
            ProcessStartTicksUtc = p.StartTime.ToUniversalTime().Ticks,
            PipeName = $"amcp-test-{unique}",
            Token = BridgeHost.NewToken(),
            StartedUtc = DateTimeOffset.UtcNow,
        };

        var commands = new List<BridgeCommand>
        {
            new("session_info", _ => new JsonObject
            {
                ["fake"] = true,
                ["product"] = product,
                ["onMainThread"] = Environment.CurrentManagedThreadId == Dispatcher.MainThreadId,
            }),
            new("echo", prm => prm.DeepClone()),
            new("boom", _ => throw new BridgeException("kaboom")),
            new("modify", _ => { Interlocked.Increment(ref ModifyCalls); return "modified"; }) { Modifies = true },
            new("execute_script", prm =>
            {
                var output = new System.Text.StringBuilder();
                var result = ScriptRunner.Run(Convert.FromBase64String(prm.ReqStr("assembly")), new object?[] { output });
                return new JsonObject { ["result"] = SafeJson.ToNode(result), ["output"] = output.ToString() };
            }) { Modifies = true, ExecutesCode = true },
        };
        if (extra is not null) commands.AddRange(extra);

        _host = new BridgeHost(Info, config ?? new BridgeConfig(), Dispatcher, commands);
        _host.Start();
        InstanceRegistry.Register(Info);
    }

    public Task<JsonNode?> Call(string method, JsonObject? p = null, string? token = null)
    {
        var info = token is null ? Info : new InstanceInfo { PipeName = Info.PipeName, Token = token, Product = Info.Product };
        return BridgeClient.CallAsync(info, method, p, TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        InstanceRegistry.Unregister(Info.InstanceId);
        _host.Dispose();
        Dispatcher.Dispose();
    }
}
