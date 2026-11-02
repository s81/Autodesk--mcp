using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AutodeskMcp.Bridge;

/// <summary>Client side of the pipe protocol, used by the MCP server.</summary>
public static class BridgeClient
{
    public static async Task<JsonNode?> CallAsync(
        InstanceInfo instance, string method, JsonObject? parameters, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var pipe = new NamedPipeClientStream(
            ".", instance.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync(5000, cts.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new BridgeException(
                $"Could not connect to {instance.Product} {instance.ProductVersion} ({instance.InstanceId}). " +
                "The application may be closing or the add-in failed to start.");
        }

        var request = new BridgeRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Token = instance.Token,
            Method = method,
            Params = parameters,
        };

        string? json;
        try
        {
            await Framing.WriteAsync(pipe, JsonSerializer.Serialize(request, BridgeJson.Options), cts.Token).ConfigureAwait(false);
            json = await Framing.ReadAsync(pipe, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BridgeException(
                $"{instance.Product} did not answer '{method}' within {timeout.TotalSeconds:0}s. " +
                "It may be busy (modal dialog open, long command running).");
        }

        if (json is null) throw new BridgeException("The add-in closed the connection without a response.");

        var response = JsonSerializer.Deserialize<BridgeResponse>(json, BridgeJson.Options)
                       ?? throw new BridgeException("Empty response from add-in.");
        if (!response.Ok) throw new BridgeException(response.Error ?? "Unknown add-in error.");
        return response.Result;
    }
}
