using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AutodeskMcp.Bridge;

/// <summary>Request sent from the MCP server to a host add-in over the named pipe.</summary>
public sealed class BridgeRequest
{
    public string Id { get; set; } = "";
    public string Token { get; set; } = "";
    public string Method { get; set; } = "";
    public JsonObject? Params { get; set; }
}

/// <summary>Response returned by the host add-in.</summary>
public sealed class BridgeResponse
{
    public string Id { get; set; } = "";
    public bool Ok { get; set; }
    public JsonNode? Result { get; set; }
    public string? Error { get; set; }
    public string? ErrorType { get; set; }
}

/// <summary>An error that is safe and meaningful to show to the model/user.</summary>
public sealed class BridgeException : Exception
{
    public BridgeException(string message) : base(message) { }
    public BridgeException(string message, Exception inner) : base(message, inner) { }
}

public static class BridgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };
}
