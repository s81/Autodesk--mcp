using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutodeskMcp.Bridge;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif

// Usage: AutodeskMcp.FakeHost <product> <instanceId>
// Registers itself in the instance registry (AUTODESK_MCP_HOME), prints "READY", and serves until stdin closes.
string product = args.Length > 0 ? args[0] : "revit";
string instanceId = args.Length > 1 ? args[1] : $"{product}-fake-{Compat.CurrentProcessId}";

using var process = Process.GetCurrentProcess();
var info = new InstanceInfo
{
    InstanceId = instanceId,
    Product = product,
    ProductVersion = "2099",
    ProductBuild = "fake",
    RuntimeVersion = RuntimeInformation.FrameworkDescription,
    ProcessId = process.Id,
    ProcessStartTicksUtc = process.StartTime.ToUniversalTime().Ticks,
    PipeName = "amcp-fake-" + Compat.RandomHex(8),
    Token = BridgeHost.NewToken(),
    StartedUtc = DateTimeOffset.UtcNow,
};

var dispatcher = new MainThreadLoop();
var commands = new[]
{
    new BridgeCommand("session_info", _ => new JsonObject
    {
        ["fake"] = true,
        ["product"] = product,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["onMainThread"] = Environment.CurrentManagedThreadId == dispatcher.ThreadId,
        ["unicode"] = "مرحبا \"quoted\" \\ ✓",
    }),
    new BridgeCommand("echo", p => p.DeepClone()),
    new BridgeCommand("execute_script", p =>
    {
        var output = new StringBuilder();
        object? result = ScriptRunner.Run(Convert.FromBase64String(p.ReqStr("assembly")), new object?[] { output });
        return new JsonObject { ["result"] = SafeJson.ToNode(result), ["output"] = output.ToString() };
    }) { Modifies = true, ExecutesCode = true },
};

using var host = new BridgeHost(info, new BridgeConfig(), dispatcher, commands, m => Console.Error.WriteLine(m));
host.Start();
InstanceRegistry.Register(info);
Console.WriteLine("READY " + info.InstanceId);
Console.Out.Flush();

try
{
    while (Console.In.ReadLine() is not null) { }
}
finally
{
    InstanceRegistry.Unregister(info.InstanceId);
    dispatcher.Dispose();
}

/// <summary>A dedicated thread standing in for the host's main thread.</summary>
internal sealed class MainThreadLoop : IMainThreadDispatcher, IDisposable
{
    private readonly BlockingCollection<PendingWork> _queue = new();
    private readonly Thread _thread;
    public int ThreadId { get; private set; }

    public MainThreadLoop()
    {
        _thread = new Thread(() =>
        {
            ThreadId = Environment.CurrentManagedThreadId;
            foreach (var item in _queue.GetConsumingEnumerable()) item.TryRun();
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
