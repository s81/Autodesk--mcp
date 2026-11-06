using System;
using System.Collections.Generic;
using System.Linq;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json;
using System.Text.Json.Nodes;
#endif

namespace AutodeskMcp.Bridge;

/// <summary>Typed, validated reads from request parameters. Errors become user-readable BridgeExceptions.</summary>
public static class Params
{
    public static string? Str(this JsonObject p, string name) =>
        p[name] is JsonValue v && v.TryGetValue(out string? s) ? s : p[name]?.ToString();

    public static string ReqStr(this JsonObject p, string name) =>
        p.Str(name) is { Length: > 0 } s ? s : throw Missing(name);

    public static bool Bool(this JsonObject p, string name, bool fallback) =>
        p[name] is JsonValue v && v.TryGetValue(out bool b) ? b : fallback;

    public static int Int(this JsonObject p, string name, int fallback) => p[name] is null ? fallback : (int)ReqDouble(p, name);

    public static long ReqLong(this JsonObject p, string name) => (long)ReqDouble(p, name);

    public static long? Long(this JsonObject p, string name) => p[name] is null ? null : (long)ReqDouble(p, name);

    public static double? Double(this JsonObject p, string name) => p[name] is null ? null : ReqDouble(p, name);

    public static double ReqDouble(this JsonObject p, string name)
    {
        var node = p[name] ?? throw Missing(name);
        return ToDouble(node, name);
    }

    public static double ToDouble(JsonNode node, string what)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out string? s) &&
                double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                return d;
            if (v.GetValueKind() == JsonValueKind.Number) return v.GetValue<double>();
        }
        throw new BridgeException($"'{what}' must be a number.");
    }

    public static JsonArray ReqArray(this JsonObject p, string name) =>
        p[name] as JsonArray ?? throw new BridgeException($"'{name}' must be an array.");

    public static JsonArray? Array(this JsonObject p, string name) => p[name] as JsonArray;

    public static List<long> ReqLongs(this JsonObject p, string name) =>
        p.ReqArray(name).Select((n, i) => (long)ToDouble(n ?? throw Missing($"{name}[{i}]"), $"{name}[{i}]")).ToList();

    public static List<string> Strings(this JsonObject p, string name) =>
        p.Array(name)?.Select(n => n?.ToString() ?? "").Where(s => s.Length > 0).ToList() ?? new List<string>();

    /// <summary>Reads [x, y] or [x, y, z]; z defaults to 0.</summary>
    public static double[] Point(JsonNode? node, string what)
    {
        if (node is not JsonArray arr || arr.Count is < 2 or > 3)
            throw new BridgeException($"'{what}' must be an array [x, y] or [x, y, z].");
        return new[]
        {
            ToDouble(arr[0]!, what + "[0]"),
            ToDouble(arr[1]!, what + "[1]"),
            arr.Count == 3 ? ToDouble(arr[2]!, what + "[2]") : 0.0,
        };
    }

    public static JsonObject Obj(JsonNode? node, string what) =>
        node as JsonObject ?? throw new BridgeException($"'{what}' must be an object.");

    private static BridgeException Missing(string name) => new($"Missing required parameter '{name}'.");
}
