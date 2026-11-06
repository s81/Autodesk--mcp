using System;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
#endif

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

/// <summary>
/// The bridge's single entry point for (de)serializing its message classes. On .NET 8+ it uses System.Text.Json;
/// on .NET Framework 4.8 (Revit/AutoCAD 2022-2024) the dependency-free MiniJson, with the same wire format:
/// camelCase property names, nulls omitted.
/// </summary>
public static class BridgeJson
{
#if NETFRAMEWORK
    public static string Serialize<T>(T value, bool indented = false) where T : class =>
        MiniJsonSerializer.Serialize(value, indented);

    public static T? Deserialize<T>(string json) where T : class, new() =>
        MiniJsonSerializer.Deserialize<T>(json);
#else
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    public static string Serialize<T>(T value, bool indented = false) where T : class =>
        JsonSerializer.Serialize(value, indented ? Indented : Options);

    public static T? Deserialize<T>(string json) where T : class, new() =>
        JsonSerializer.Deserialize<T>(json, Options);
#endif
}
