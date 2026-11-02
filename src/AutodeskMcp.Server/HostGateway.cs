using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AutodeskMcp.Bridge;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace AutodeskMcp.Server;

/// <summary>Resolves the target host instance and forwards a call to its add-in.</summary>
public sealed class HostGateway(ILogger<HostGateway> logger)
{
    public const string Revit = "revit";
    public const string AutoCAD = "autocad";

    /// <summary>Upper bound for one round trip. The add-in itself cancels main-thread work after its own timeout.</summary>
    public static TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(
        int.TryParse(Environment.GetEnvironmentVariable("AUTODESK_MCP_TIMEOUT_SECONDS"), out int s) && s > 0 ? s : 300);

    public InstanceInfo Resolve(string product, string? instanceId)
    {
        try { return InstanceRegistry.Resolve(product, instanceId); }
        catch (BridgeException ex) { throw new McpException(ex.Message); }
    }

    public async Task<JsonNode?> CallNodeAsync(InstanceInfo instance, string method, JsonObject? parameters, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("{Product} {Instance} <- {Method}", instance.Product, instance.InstanceId, method);
            return await BridgeClient.CallAsync(instance, method, parameters, CallTimeout, ct);
        }
        catch (BridgeException ex)
        {
            // McpException messages are returned to the model as a tool error; others are masked by the SDK.
            throw new McpException(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new McpException($"Pipe error talking to {instance.Product} ({instance.InstanceId}): {ex.Message}");
        }
    }

    public async Task<string> CallAsync(string product, string? instanceId, string method, object? parameters, CancellationToken ct)
    {
        var instance = Resolve(product, instanceId);
        JsonObject? p = parameters switch
        {
            null => null,
            JsonObject o => o,
            _ => JsonSerializer.SerializeToNode(parameters, BridgeJson.Options) as JsonObject,
        };
        var result = await CallNodeAsync(instance, method, p, ct);
        return result?.ToJsonString(BridgeJson.Indented) ?? "null";
    }
}

