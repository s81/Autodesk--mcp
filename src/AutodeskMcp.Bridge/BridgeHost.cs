using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace AutodeskMcp.Bridge;

/// <summary>One callable operation exposed by a host add-in.</summary>
public sealed class BridgeCommand
{
    public BridgeCommand(string name, Func<JsonObject, JsonNode?> handler)
    {
        Name = name;
        Handler = handler;
    }

    public string Name { get; }
    public Func<JsonObject, JsonNode?> Handler { get; }

    /// <summary>Changes the model/drawing. Rejected when <see cref="BridgeConfig.AllowModifications"/> is false.</summary>
    public bool Modifies { get; init; }

    /// <summary>Runs user-supplied code. Rejected when <see cref="BridgeConfig.AllowCodeExecution"/> is false.</summary>
    public bool ExecutesCode { get; init; }

    /// <summary>
    /// Almost every Revit/AutoCAD API call must run on the host's main thread. Only set false for
    /// handlers that touch no host API (e.g. listing loaded assemblies).
    /// </summary>
    public bool RequiresMainThread { get; init; } = true;
}

/// <summary>Marshals work onto the host application's main (API) thread.</summary>
public interface IMainThreadDispatcher
{
    /// <summary>
    /// Queues <paramref name="work"/> on the main thread. If it has not *started* within
    /// <paramref name="timeout"/> it is cancelled and guaranteed never to run.
    /// </summary>
    Task<JsonNode?> InvokeAsync(Func<JsonNode?> work, TimeSpan timeout, CancellationToken ct);
}

/// <summary>
/// Named-pipe server that runs inside Revit/AutoCAD. Accepts one request per connection,
/// authenticates it with the per-instance token, enforces <see cref="BridgeConfig"/> and dispatches
/// to the main thread.
/// </summary>
public sealed class BridgeHost : IDisposable
{
    private readonly InstanceInfo _info;
    private readonly BridgeConfig _config;
    private readonly IMainThreadDispatcher _dispatcher;
    private readonly Dictionary<string, BridgeCommand> _commands;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private volatile NamedPipeServerStream? _listening;

    public BridgeHost(InstanceInfo info, BridgeConfig config, IMainThreadDispatcher dispatcher,
        IEnumerable<BridgeCommand> commands, Action<string>? log = null)
    {
        _info = info;
        _config = config;
        _dispatcher = dispatcher;
        _log = log ?? (_ => { });
        _commands = new Dictionary<string, BridgeCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in commands) _commands[c.Name] = c;

        _commands["__ping"] = new BridgeCommand("__ping", _ => new JsonObject
        {
            ["instanceId"] = _info.InstanceId,
            ["product"] = _info.Product,
            ["productVersion"] = _info.ProductVersion,
            ["productBuild"] = _info.ProductBuild,
            ["addinVersion"] = _info.AddinVersion,
            ["runtimeVersion"] = _info.RuntimeVersion,
            ["allowModifications"] = _config.AllowModifications,
            ["allowCodeExecution"] = _config.AllowCodeExecution,
            ["methods"] = new JsonArray(_commands.Keys.OrderBy(k => k).Select(k => (JsonNode?)k).ToArray()),
        }) { RequiresMainThread = false };

        _commands["__script_references"] = new BridgeCommand("__script_references",
            _ => new JsonArray(ScriptRunner.GetReferencePaths().Select(p => (JsonNode?)p).ToArray()))
        { RequiresMainThread = false };
    }

    public static string NewToken() => Compat.RandomHex(32);

    public void Start()
    {
        _acceptLoop = Task.Run(AcceptLoopAsync);
        _log($"Listening on pipe '{_info.PipeName}'.");
    }

    private async Task AcceptLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreatePipe();
                _listening = server;

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var connected = server;
                server = null;
                _ = Task.Run(() => HandleConnectionAsync(connected, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _log($"Pipe accept error: {ex.Message}");
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    /// <summary>A pipe that only the current Windows user can open.</summary>
    private NamedPipeServerStream CreatePipe()
    {
#if NETFRAMEWORK
        // .NET Framework has no PipeOptions.CurrentUserOnly, so reproduce what .NET does for it: the token's
        // default owner becomes the pipe's owner and the only identity allowed in. The .NET 10 client's
        // CurrentUserOnly check compares the pipe owner with WindowsIdentity.GetCurrent().Owner, which is the
        // user for a normal process but BUILTIN\Administrators for an elevated ("Run as administrator") one,
        // so using the user SID here would make elevated Revit/AutoCAD unreachable.
        SecurityIdentifier owner;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            owner = identity.Owner ?? identity.User
                    ?? throw new InvalidOperationException("Cannot determine the current Windows user.");
        }
        var security = new PipeSecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        return new NamedPipeServerStream(
            _info.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
#else
        // CurrentUserOnly: the OS rejects clients running as a different user.
        return new NamedPipeServerStream(
            _info.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
#endif
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            BridgeResponse response;
            string requestId = "";
            try
            {
                string? json = await Framing.ReadAsync(pipe, ct).ConfigureAwait(false);
                if (json is null) return;
                var request = BridgeJson.Deserialize<BridgeRequest>(json)
                              ?? throw new BridgeException("Empty request.");
                requestId = request.Id;
                response = await ExecuteAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                response = Fail(requestId, ex);
            }

            try
            {
                await Framing.WriteAsync(pipe, BridgeJson.Serialize(response), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log($"Failed to write response: {ex.Message}");
            }
        }
    }

    /// <summary>Exposed for in-process tests.</summary>
    public async Task<BridgeResponse> ExecuteAsync(BridgeRequest request, CancellationToken ct)
    {
        if (!TokenMatches(request.Token))
            return Fail(request.Id, new BridgeException("Authentication failed (bad token)."));

        if (!_commands.TryGetValue(request.Method, out var command))
            return Fail(request.Id, new BridgeException($"Unknown method '{request.Method}'."));

        if (command.Modifies && !_config.AllowModifications)
            return Fail(request.Id, new BridgeException(
                $"'{command.Name}' modifies the model, but modifications are disabled in {BridgeConfig.FilePath}."));

        if (command.ExecutesCode && !_config.AllowCodeExecution)
            return Fail(request.Id, new BridgeException(
                $"Code execution is disabled in {BridgeConfig.FilePath} (allowCodeExecution=false)."));

        var parameters = request.Params ?? new JsonObject();
        try
        {
            JsonNode? result = command.RequiresMainThread
                ? await _dispatcher.InvokeAsync(() => command.Handler(parameters),
                    TimeSpan.FromSeconds(Math.Max(1, _config.MainThreadTimeoutSeconds)), ct).ConfigureAwait(false)
                : command.Handler(parameters);

            return new BridgeResponse { Id = request.Id, Ok = true, Result = result };
        }
        catch (Exception ex)
        {
            _log($"{command.Name} failed: {ex}");
            return Fail(request.Id, ex);
        }
    }

    private bool TokenMatches(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        byte[] a = Encoding.UTF8.GetBytes(token);
        byte[] b = Encoding.UTF8.GetBytes(_info.Token);
        return Compat.FixedTimeEquals(a, b);
    }

    private static BridgeResponse Fail(string id, Exception ex)
    {
        var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } ie } ? ie : ex;
        return new BridgeResponse
        {
            Id = id,
            Ok = false,
            Error = (inner is BridgeException ? inner.Message : $"{inner.GetType().Name}: {inner.Message}") + ScriptFrames(inner),
            ErrorType = inner.GetType().FullName,
        };
    }

    /// <summary>Stack frames that point into an execute_csharp snippet, so the model can locate the failing line.</summary>
    private static string ScriptFrames(Exception ex)
    {
        var frames = (ex.StackTrace ?? "").Split(new[] { '\n' })
            .Select(l => l.Trim())
            .Where(l => l.IndexOf("script.cs", StringComparison.Ordinal) >= 0 || l.IndexOf(ScriptRunner.EntryType + "." + ScriptRunner.EntryMethod, StringComparison.Ordinal) >= 0)
            .Take(5)
            .ToList();
        return frames.Count == 0 ? "" : "\n" + string.Join("\n", frames);
    }

    public void Dispose()
    {
        _cts.Cancel();
        // .NET Framework does not always abort a pending WaitForConnectionAsync on cancellation.
        try { _listening?.Dispose(); } catch { }
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
