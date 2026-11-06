using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace AutodeskMcp.Bridge.Json;

/// <summary>
/// Reflection-based (de)serializer for the bridge's simple message classes (BridgeRequest, BridgeResponse,
/// InstanceInfo, BridgeConfig), producing the same wire format as System.Text.Json with camelCase names and
/// null values omitted. Supports string, bool, int, long, double, DateTimeOffset and JsonNode properties.
/// </summary>
public static class MiniJsonSerializer
{
    public static string Serialize(object value, bool indented = false) => ToNode(value).ToJsonString(indented);

    public static JsonObject ToNode(object value)
    {
        var obj = new JsonObject();
        foreach (var prop in Properties(value.GetType()))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            JsonNode? node = ToNodeValue(prop.GetValue(value));
            if (node is null) continue; // WhenWritingNull
            obj[CamelCase(prop.Name)] = node;
        }
        return obj;
    }

    public static T? Deserialize<T>(string json) where T : class, new()
    {
        if (JsonNode.Parse(json) is not JsonObject obj) return null;
        return FromNode<T>(obj);
    }

    public static T FromNode<T>(JsonObject obj) where T : class, new()
    {
        var result = new T();
        foreach (var prop in Properties(typeof(T)))
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
            if (!TryGetProperty(obj, prop.Name, out JsonNode? node)) continue;
            prop.SetValue(result, FromNodeValue(node, prop.PropertyType, prop.Name));
        }
        return result;
    }

    private static IEnumerable<PropertyInfo> Properties(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static bool TryGetProperty(JsonObject obj, string clrName, out JsonNode? node)
    {
        if (obj.TryGetPropertyValue(CamelCase(clrName), out node)) return true;
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, clrName, StringComparison.OrdinalIgnoreCase)) { node = kv.Value; return true; }
        }
        node = null;
        return false;
    }

    private static JsonNode? ToNodeValue(object? v) => v switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("o", CultureInfo.InvariantCulture)),
        _ => throw new NotSupportedException($"MiniJsonSerializer does not support {v.GetType().Name}."),
    };

    private static object? FromNodeValue(JsonNode? node, Type type, string name)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        if (node is null)
        {
            if (!t.IsValueType || Nullable.GetUnderlyingType(type) is not null) return null;
            throw new FormatException($"'{name}' cannot be null.");
        }

        if (typeof(JsonNode).IsAssignableFrom(t))
        {
            if (!t.IsInstanceOfType(node)) throw new FormatException($"'{name}' must be a JSON {t.Name}.");
            return node;
        }

        var value = node as JsonValue ?? throw new FormatException($"'{name}' must be a JSON value.");
        if (t == typeof(DateTimeOffset))
        {
            if (value.TryGetValue(out string? s) &&
                DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;
            throw new FormatException($"'{name}' must be an ISO 8601 date.");
        }

        var method = typeof(JsonValue).GetMethod(nameof(JsonValue.TryGetValue))!.MakeGenericMethod(t);
        object?[] args = { null };
        if ((bool)method.Invoke(value, args)!) return args[0];
        throw new FormatException($"'{name}' must be a {t.Name}.");
    }

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
}
