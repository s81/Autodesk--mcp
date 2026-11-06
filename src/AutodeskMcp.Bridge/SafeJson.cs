using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif

namespace AutodeskMcp.Bridge;

/// <summary>
/// Converts arbitrary objects (including script return values and host API objects, which are full of
/// cycles) into JSON safely: bounded depth, bounded collection size, and a host-specific hook for API types.
/// </summary>
public static class SafeJson
{
    public delegate JsonNode? CustomConverter(object value, Func<object?, JsonNode?> recurse);

    public static JsonNode? ToNode(object? value, CustomConverter? custom = null, int maxDepth = 6, int maxItems = 2000)
        => Convert(value, custom, maxDepth, maxItems, 0);

    private static JsonNode? Convert(object? value, CustomConverter? custom, int maxDepth, int maxItems, int depth)
    {
        switch (value)
        {
            case null: return null;
            case JsonNode node: return node.DeepClone();
            case string s: return JsonValue.Create(s);
            case bool b: return JsonValue.Create(b);
            case char c: return JsonValue.Create(c.ToString());
            case double d: return Compat.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
            case float f: return Compat.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
            case decimal m: return JsonValue.Create(m);
            case int i: return JsonValue.Create(i);
            case long l: return JsonValue.Create(l);
            case short sh: return JsonValue.Create(sh);
            case byte by: return JsonValue.Create(by);
            case uint ui: return JsonValue.Create(ui);
            case ulong ul: return JsonValue.Create(ul);
            case Enum e: return JsonValue.Create(e.ToString());
            case Guid g: return JsonValue.Create(g.ToString());
            case DateTime dt: return JsonValue.Create(dt.ToString("o"));
            case DateTimeOffset dto: return JsonValue.Create(dto.ToString("o"));
            case TimeSpan ts: return JsonValue.Create(ts.ToString());
            case Type t: return JsonValue.Create(t.FullName);
        }

        if (depth >= maxDepth) return JsonValue.Create(SafeToString(value));

        JsonNode? Recurse(object? v) => Convert(v, custom, maxDepth, maxItems, depth + 1);

        if (custom is not null)
        {
            JsonNode? converted;
            try { converted = custom(value, Recurse); }
            catch (Exception ex) { converted = JsonValue.Create($"<{value.GetType().Name}: {ex.Message}>"); }
            if (converted is not null) return converted;
        }

        if (value is IDictionary dict)
        {
            var obj = new JsonObject();
            int n = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (n++ >= maxItems) { obj["__truncated"] = true; break; }
                obj[entry.Key?.ToString() ?? "null"] = Recurse(entry.Value);
            }
            return obj;
        }

        if (value is ITuple tuple)
        {
            var arr = new JsonArray();
            for (int i = 0; i < tuple.Length; i++) arr.Add(Recurse(tuple[i]));
            return arr;
        }

        if (value is IEnumerable enumerable)
        {
            var arr = new JsonArray();
            int n = 0;
            foreach (object? item in enumerable)
            {
                if (n++ >= maxItems) { arr.Add(JsonValue.Create($"... truncated after {maxItems} items")); break; }
                arr.Add(Recurse(item));
            }
            return arr;
        }

        Type type = value.GetType();
        // Reflect only over data-shaped types that the script itself defined (anonymous types, records,
        // POCOs from the collectible script assembly). Host API objects are never reflected blindly:
        // their getters can be expensive, throw, or require a transaction.
        if (IsAnonymous(type) || ScriptRunner.IsScriptAssembly(type.Assembly) || type.Assembly.IsDynamic)
        {
            var obj = new JsonObject();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                try { obj[prop.Name] = Recurse(prop.GetValue(value)); }
                catch (Exception ex) { obj[prop.Name] = $"<error: {ex.InnerException?.Message ?? ex.Message}>"; }
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                obj[field.Name] = Recurse(field.GetValue(value));
            return obj;
        }

        return JsonValue.Create(SafeToString(value));
    }

    private static bool IsAnonymous(Type t) =>
        t.Name.IndexOf("AnonymousType", StringComparison.Ordinal) >= 0 &&
        t.GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Any();

    private static string SafeToString(object value)
    {
        try { return value.ToString() ?? value.GetType().Name; }
        catch { return value.GetType().FullName ?? "object"; }
    }
}
