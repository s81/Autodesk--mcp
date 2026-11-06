using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// A small, dependency-free JSON document model used by the add-ins that run on .NET Framework 4.8
// (Revit/AutoCAD 2022-2024). Those hosts have no built-in JSON support, and shipping System.Text.Json
// would add ~7 DLLs that can clash with other add-ins in the same process.
//
// The public surface deliberately mirrors the subset of System.Text.Json.Nodes that the shared bridge
// and add-in code uses (JsonNode/JsonObject/JsonArray/JsonValue, implicit conversions, TryGetValue,
// GetValue, GetValueKind, DeepClone, Parse, ToJsonString), so the same source compiles against either.
// It is compiled for every target framework so it can be tested on all of them.
namespace AutodeskMcp.Bridge.Json;

public enum JsonValueKind
{
    Undefined = 0,
    Object = 1,
    Array = 2,
    String = 3,
    Number = 4,
    True = 5,
    False = 6,
    Null = 7,
}

public abstract class JsonNode
{
    private protected JsonNode() { }

    public abstract JsonValueKind GetValueKind();

    public abstract JsonNode DeepClone();

    public JsonObject AsObject() =>
        this as JsonObject ?? throw new InvalidOperationException($"The node is a {GetValueKind()}, not an Object.");

    public JsonArray AsArray() =>
        this as JsonArray ?? throw new InvalidOperationException($"The node is a {GetValueKind()}, not an Array.");

    public JsonValue AsValue() =>
        this as JsonValue ?? throw new InvalidOperationException($"The node is a {GetValueKind()}, not a value.");

    public JsonNode? this[string propertyName]
    {
        get => AsObject()[propertyName];
        set => AsObject()[propertyName] = value;
    }

    public JsonNode? this[int index]
    {
        get => AsArray()[index];
        set => AsArray()[index] = value;
    }

    public T GetValue<T>() => AsValue().GetValue<T>();

    /// <summary>Compact JSON, or indented when <paramref name="indented"/> is true.</summary>
    public string ToJsonString(bool indented = false)
    {
        var sb = new StringBuilder();
        JsonWriter.Write(sb, this, indented, 0);
        return sb.ToString();
    }

    /// <summary>Like System.Text.Json: a string value returns its raw text, anything else its (indented) JSON.</summary>
    public override string ToString() => ToJsonString(indented: true);

    public static JsonNode? Parse(string json) => JsonReader.Parse(json);

    // Implicit conversions (same set as System.Text.Json.Nodes.JsonNode).
    public static implicit operator JsonNode(bool value) => JsonValue.Create(value);
    public static implicit operator JsonNode(byte value) => JsonValue.Create(value);
    public static implicit operator JsonNode(short value) => JsonValue.Create(value);
    public static implicit operator JsonNode(int value) => JsonValue.Create(value);
    public static implicit operator JsonNode(long value) => JsonValue.Create(value);
    public static implicit operator JsonNode(uint value) => JsonValue.Create(value);
    public static implicit operator JsonNode(ulong value) => JsonValue.Create(value);
    public static implicit operator JsonNode(float value) => JsonValue.Create(value);
    public static implicit operator JsonNode(double value) => JsonValue.Create(value);
    public static implicit operator JsonNode(decimal value) => JsonValue.Create(value);
    public static implicit operator JsonNode?(string? value) => JsonValue.Create(value);
    public static implicit operator JsonNode?(bool? value) => value is bool v ? JsonValue.Create(v) : null;
    public static implicit operator JsonNode?(int? value) => value is int v ? JsonValue.Create(v) : null;
    public static implicit operator JsonNode?(long? value) => value is long v ? JsonValue.Create(v) : null;
    public static implicit operator JsonNode?(double? value) => value is double v ? JsonValue.Create(v) : null;
}

public sealed class JsonObject : JsonNode, IEnumerable<KeyValuePair<string, JsonNode?>>
{
    private readonly List<KeyValuePair<string, JsonNode?>> _items = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public int Count => _items.Count;

    public new JsonNode? this[string propertyName]
    {
        get => _index.TryGetValue(propertyName, out int i) ? _items[i].Value : null;
        set
        {
            if (_index.TryGetValue(propertyName, out int i)) _items[i] = new KeyValuePair<string, JsonNode?>(propertyName, value);
            else
            {
                _index[propertyName] = _items.Count;
                _items.Add(new KeyValuePair<string, JsonNode?>(propertyName, value));
            }
        }
    }

    public void Add(string propertyName, JsonNode? value)
    {
        if (_index.ContainsKey(propertyName)) throw new ArgumentException($"An item with the key '{propertyName}' has already been added.");
        this[propertyName] = value;
    }

    public bool ContainsKey(string propertyName) => _index.ContainsKey(propertyName);

    public bool TryGetPropertyValue(string propertyName, out JsonNode? value)
    {
        bool found = _index.TryGetValue(propertyName, out int i);
        value = found ? _items[i].Value : null;
        return found;
    }

    public bool Remove(string propertyName)
    {
        if (!_index.TryGetValue(propertyName, out int i)) return false;
        _items.RemoveAt(i);
        _index.Clear();
        for (int k = 0; k < _items.Count; k++) _index[_items[k].Key] = k;
        return true;
    }

    public override JsonValueKind GetValueKind() => JsonValueKind.Object;

    public override JsonNode DeepClone()
    {
        var copy = new JsonObject();
        foreach (var kv in _items) copy[kv.Key] = kv.Value?.DeepClone();
        return copy;
    }

    public IEnumerator<KeyValuePair<string, JsonNode?>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class JsonArray : JsonNode, IList<JsonNode?>
{
    private readonly List<JsonNode?> _items;

    public JsonArray() => _items = new List<JsonNode?>();

    public JsonArray(params JsonNode?[] items) => _items = new List<JsonNode?>(items);

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public new JsonNode? this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public void Add(JsonNode? item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(JsonNode? item) => _items.Contains(item);
    public void CopyTo(JsonNode?[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public int IndexOf(JsonNode? item) => _items.IndexOf(item);
    public void Insert(int index, JsonNode? item) => _items.Insert(index, item);
    public bool Remove(JsonNode? item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public override JsonValueKind GetValueKind() => JsonValueKind.Array;

    public override JsonNode DeepClone()
    {
        var copy = new JsonArray();
        foreach (var item in _items) copy.Add(item?.DeepClone());
        return copy;
    }

    public IEnumerator<JsonNode?> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A string, number or boolean. (JSON null is represented by a C# null node, as in System.Text.Json.)</summary>
public sealed class JsonValue : JsonNode
{
    private readonly JsonValueKind _kind;
    private readonly string? _string;   // string value, or the invariant text of a number
    private readonly object? _number;   // the CLR number it was created from (null when parsed)

    private JsonValue(JsonValueKind kind, string? text, object? number)
    {
        _kind = kind;
        _string = text;
        _number = number;
    }

    internal static JsonValue FromNumberText(string text) => new(JsonValueKind.Number, text, null);

    public static JsonValue Create(bool value) => new(value ? JsonValueKind.True : JsonValueKind.False, null, null);
    public static JsonValue? Create(string? value) => value is null ? null : new JsonValue(JsonValueKind.String, value, null);
    public static JsonValue Create(byte value) => Num(value, value.ToString(CultureInfo.InvariantCulture));
    public static JsonValue Create(short value) => Num(value, value.ToString(CultureInfo.InvariantCulture));
    public static JsonValue Create(int value) => Num(value, value.ToString(CultureInfo.InvariantCulture));
    public static JsonValue Create(long value) => Num(value, value.ToString(CultureInfo.InvariantCulture));
    public static JsonValue Create(uint value) => Num(value, value.ToString(CultureInfo.InvariantCulture));
    public static JsonValue Create(ulong value) => Num(value, value.ToString(CultureInfo.InvariantCulture));
    public static JsonValue Create(decimal value) => Num(value, value.ToString(CultureInfo.InvariantCulture));

    public static JsonValue Create(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException(".NET number values such as positive and negative infinity cannot be written as valid JSON.");
        return Num(value, value.ToString("R", CultureInfo.InvariantCulture));
    }

    public static JsonValue Create(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentException(".NET number values such as positive and negative infinity cannot be written as valid JSON.");
        return Num(value, value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static JsonValue Num(object boxed, string text) => new(JsonValueKind.Number, text, boxed);

    public override JsonValueKind GetValueKind() => _kind;

    public override JsonNode DeepClone() => new JsonValue(_kind, _string, _number);

    internal string RawText => _kind switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => _string ?? "",
    };

    public override string ToString() => _kind == JsonValueKind.String ? _string! : RawText;

    public new T GetValue<T>()
    {
        if (TryGetValue(out T value)) return value;
        throw new InvalidOperationException($"A JSON {_kind} value cannot be converted to {typeof(T).Name}.");
    }

    public bool TryGetValue<T>(out T value)
    {
        if (TryConvert(typeof(T), out object? result))
        {
            value = (T)result!;
            return true;
        }
        value = default!;
        return false;
    }

    private bool TryConvert(Type t, out object? result)
    {
        result = null;
        Type target = Nullable.GetUnderlyingType(t) ?? t;

        if (target == typeof(object)) { result = ToObject(); return true; }

        if (target == typeof(string))
        {
            if (_kind != JsonValueKind.String) return false;
            result = _string;
            return true;
        }

        if (target == typeof(bool))
        {
            if (_kind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            result = _kind == JsonValueKind.True;
            return true;
        }

        if (_kind != JsonValueKind.Number) return false;
        var ic = CultureInfo.InvariantCulture;
        const NumberStyles integer = NumberStyles.Integer;
        const NumberStyles floating = NumberStyles.Float;
        string text = _string!;

        if (target == typeof(double)) { if (!double.TryParse(text, floating, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(float)) { if (!float.TryParse(text, floating, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(decimal)) { if (!decimal.TryParse(text, floating, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(long)) { if (!long.TryParse(text, integer, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(int)) { if (!int.TryParse(text, integer, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(short)) { if (!short.TryParse(text, integer, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(byte)) { if (!byte.TryParse(text, integer, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(uint)) { if (!uint.TryParse(text, integer, ic, out var v)) return false; result = v; return true; }
        if (target == typeof(ulong)) { if (!ulong.TryParse(text, integer, ic, out var v)) return false; result = v; return true; }
        return false;
    }

    private object? ToObject() => _kind switch
    {
        JsonValueKind.String => _string,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => _number ?? (long.TryParse(_string, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)
            ? l
            : double.Parse(_string!, NumberStyles.Float, CultureInfo.InvariantCulture)),
    };
}

internal static class JsonWriter
{
    public static void Write(StringBuilder sb, JsonNode? node, bool indented, int depth)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonValue v:
                if (v.GetValueKind() == JsonValueKind.String) WriteString(sb, v.ToString());
                else sb.Append(v.RawText);
                break;
            case JsonObject o:
                if (o.Count == 0) { sb.Append("{}"); break; }
                sb.Append('{');
                bool first = true;
                foreach (var kv in o)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, indented, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(indented ? ": " : ":");
                    Write(sb, kv.Value, indented, depth + 1);
                }
                NewLine(sb, indented, depth);
                sb.Append('}');
                break;
            case JsonArray a:
                if (a.Count == 0) { sb.Append("[]"); break; }
                sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indented, depth + 1);
                    Write(sb, a[i], indented, depth + 1);
                }
                NewLine(sb, indented, depth);
                sb.Append(']');
                break;
        }
    }

    private static void NewLine(StringBuilder sb, bool indented, int depth)
    {
        if (!indented) return;
        sb.Append('\n');
        sb.Append(' ', depth * 2);
    }

    public static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

/// <summary>Strict RFC 8259 parser.</summary>
internal sealed class JsonReader
{
    private const int MaxDepth = 256;
    private readonly string _s;
    private int _pos;

    private JsonReader(string s) => _s = s;

    public static JsonNode? Parse(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        var r = new JsonReader(json);
        r.SkipWhitespace();
        var node = r.ReadValue(0);
        r.SkipWhitespace();
        if (r._pos != r._s.Length) throw r.Error("Unexpected data after the JSON value");
        return node;
    }

    private FormatException Error(string message) => new($"Invalid JSON: {message} at position {_pos}.");

    private void SkipWhitespace()
    {
        while (_pos < _s.Length && _s[_pos] is ' ' or '\t' or '\n' or '\r') _pos++;
    }

    private JsonNode? ReadValue(int depth)
    {
        if (depth > MaxDepth) throw Error("Maximum depth exceeded");
        if (_pos >= _s.Length) throw Error("Unexpected end of input");
        char c = _s[_pos];
        switch (c)
        {
            case '{': return ReadObject(depth);
            case '[': return ReadArray(depth);
            case '"': return JsonValue.Create(ReadString())!;
            case 't': Expect("true"); return JsonValue.Create(true);
            case 'f': Expect("false"); return JsonValue.Create(false);
            case 'n': Expect("null"); return null;
            default:
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw Error($"Unexpected character '{c}'");
        }
    }

    private void Expect(string literal)
    {
        if (string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) != 0) throw Error($"Expected '{literal}'");
        _pos += literal.Length;
    }

    private JsonObject ReadObject(int depth)
    {
        var obj = new JsonObject();
        _pos++; // {
        SkipWhitespace();
        if (_pos < _s.Length && _s[_pos] == '}') { _pos++; return obj; }
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _s.Length || _s[_pos] != '"') throw Error("Expected a property name");
            string key = ReadString();
            SkipWhitespace();
            if (_pos >= _s.Length || _s[_pos] != ':') throw Error("Expected ':'");
            _pos++;
            SkipWhitespace();
            obj[key] = ReadValue(depth + 1);
            SkipWhitespace();
            if (_pos >= _s.Length) throw Error("Unexpected end of input in object");
            if (_s[_pos] == ',') { _pos++; continue; }
            if (_s[_pos] == '}') { _pos++; return obj; }
            throw Error("Expected ',' or '}'");
        }
    }

    private JsonArray ReadArray(int depth)
    {
        var arr = new JsonArray();
        _pos++; // [
        SkipWhitespace();
        if (_pos < _s.Length && _s[_pos] == ']') { _pos++; return arr; }
        while (true)
        {
            SkipWhitespace();
            arr.Add(ReadValue(depth + 1));
            SkipWhitespace();
            if (_pos >= _s.Length) throw Error("Unexpected end of input in array");
            if (_s[_pos] == ',') { _pos++; continue; }
            if (_s[_pos] == ']') { _pos++; return arr; }
            throw Error("Expected ',' or ']'");
        }
    }

    private string ReadString()
    {
        _pos++; // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (_pos >= _s.Length) throw Error("Unterminated string");
            char c = _s[_pos++];
            if (c == '"') return sb.ToString();
            if (c < 0x20) throw Error("Control character in string");
            if (c != '\\') { sb.Append(c); continue; }
            if (_pos >= _s.Length) throw Error("Unterminated escape");
            char e = _s[_pos++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (_pos + 4 > _s.Length) throw Error("Incomplete \\u escape");
                    if (!int.TryParse(_s.Substring(_pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                        throw Error("Invalid \\u escape");
                    sb.Append((char)code); // surrogate pairs arrive as two escapes and recombine naturally
                    _pos += 4;
                    break;
                default:
                    throw Error($"Invalid escape '\\{e}'");
            }
        }
    }

    private static bool IsDigit(char c) => c >= '0' && c <= '9'; // ASCII only, unlike char.IsDigit

    private JsonValue ReadNumber()
    {
        int start = _pos;
        if (_s[_pos] == '-') _pos++;
        if (_pos >= _s.Length) throw Error("Invalid number");
        if (_s[_pos] == '0') _pos++;
        else if (_s[_pos] >= '1' && _s[_pos] <= '9') { while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++; }
        else throw Error("Invalid number");

        if (_pos < _s.Length && _s[_pos] == '.')
        {
            _pos++;
            int digits = _pos;
            while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++;
            if (_pos == digits) throw Error("Invalid number");
        }
        if (_pos < _s.Length && (_s[_pos] == 'e' || _s[_pos] == 'E'))
        {
            _pos++;
            if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++;
            int digits = _pos;
            while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++;
            if (_pos == digits) throw Error("Invalid number");
        }
        return JsonValue.FromNumberText(_s.Substring(start, _pos - start));
    }
}
