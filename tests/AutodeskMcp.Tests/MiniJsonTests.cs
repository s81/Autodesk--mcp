using System.Globalization;
using AutodeskMcp.Bridge;
using Xunit;
using Mini = AutodeskMcp.Bridge.Json;

namespace AutodeskMcp.Tests;

/// <summary>
/// MiniJson is what the 2022-2024 add-ins use instead of System.Text.Json. These tests run on every target
/// framework; on .NET 10 they additionally check wire compatibility against System.Text.Json.
/// </summary>
public sealed class MiniJsonTests
{
    private const string Sample =
        "{\"s\":\"a\\\"b\\\\c\\n\\t\\u00e9\\ud83d\\ude00 مرحبا\",\"i\":-42,\"big\":9007199254740993,\"d\":3.25,\"e\":-1.5E+20," +
        "\"t\":true,\"f\":false,\"n\":null,\"arr\":[1,\"two\",[],{},null],\"obj\":{\"nested\":{\"deep\":[true]}}}";

    [Fact]
    public void Parses_and_writes_all_value_kinds()
    {
        var node = Mini.JsonNode.Parse(Sample)!.AsObject();
        Assert.Equal("a\"b\\c\n\té😀 مرحبا", node["s"]!.GetValue<string>());
        Assert.Equal(-42, node["i"]!.GetValue<int>());
        Assert.Equal(9007199254740993L, node["big"]!.GetValue<long>()); // beyond double precision: kept exact
        Assert.Equal(3.25, node["d"]!.GetValue<double>());
        Assert.Equal(-1.5e20, node["e"]!.GetValue<double>());
        Assert.True(node["t"]!.GetValue<bool>());
        Assert.False(node["f"]!.GetValue<bool>());
        Assert.True(node.ContainsKey("n"));
        Assert.Null(node["n"]);
        Assert.Equal(5, node["arr"]!.AsArray().Count);
        Assert.True(node["obj"]!["nested"]!["deep"]![0]!.GetValue<bool>());

        // Writing and re-reading gives the same document.
        string written = node.ToJsonString();
        Assert.Equal(written, Mini.JsonNode.Parse(written)!.ToJsonString());
        Assert.Equal(written, Mini.JsonNode.Parse(node.ToJsonString(indented: true))!.ToJsonString());
    }

    [Theory]
    [InlineData("{\"a\":1,}")]
    [InlineData("[1,2,]")]
    [InlineData("{'a':1}")]
    [InlineData("{a:1}")]
    [InlineData("NaN")]
    [InlineData("01")]
    [InlineData("1.")]
    [InlineData("-")]
    [InlineData("\"tab\there\"")]
    [InlineData("\"unterminated")]
    [InlineData("{\"a\":1} extra")]
    [InlineData("\"bad \\x escape\"")]
    [InlineData("tru")]
    [InlineData("")]
    public void Rejects_invalid_json(string json) => Assert.Throws<FormatException>(() => Mini.JsonNode.Parse(json));

    [Fact]
    public void Value_conversions_follow_json_types()
    {
        var number = Mini.JsonNode.Parse("123")!.AsValue();
        Assert.True(number.TryGetValue(out double d) && d == 123);
        Assert.True(number.TryGetValue(out long l) && l == 123);
        Assert.False(number.TryGetValue(out string? _));
        Assert.False(Mini.JsonNode.Parse("1.5")!.AsValue().TryGetValue(out int _));
        Assert.False(Mini.JsonNode.Parse("4294967296")!.AsValue().TryGetValue(out int _)); // overflow
        Assert.False(Mini.JsonNode.Parse("\"5\"")!.AsValue().TryGetValue(out double _));
        Assert.Equal(Mini.JsonValueKind.Number, number.GetValueKind());
        Assert.Throws<InvalidOperationException>(() => Mini.JsonNode.Parse("\"x\"")!.GetValue<bool>());

        // ToString matches System.Text.Json: raw text for values, JSON for containers.
        Assert.Equal("hello", Mini.JsonValue.Create("hello")!.ToString());
        Assert.Equal("1.5", Mini.JsonValue.Create(1.5).ToString());
        Assert.Equal("true", Mini.JsonValue.Create(true).ToString());
        Assert.StartsWith("{", new Mini.JsonObject { ["a"] = 1 }.ToString());
    }

    [Fact]
    public void Implicit_conversions_and_containers_behave_like_system_text_json()
    {
        Mini.JsonNode? nullString = (string?)null;
        Assert.Null(nullString);
        var obj = new Mini.JsonObject { ["i"] = 1, ["l"] = 5_000_000_000L, ["d"] = 0.1, ["b"] = true, ["s"] = "x", ["n"] = null };
        obj["i"] = 2; // overwrite keeps order
        Assert.Equal("{\"i\":2,\"l\":5000000000,\"d\":0.1,\"b\":true,\"s\":\"x\",\"n\":null}", obj.ToJsonString());

        var arr = new Mini.JsonArray(1, "two", null, new Mini.JsonObject());
        Assert.Equal("[1,\"two\",null,{}]", arr.ToJsonString());

        var clone = (Mini.JsonObject)obj.DeepClone();
        clone["s"] = "changed";
        Assert.Equal("x", obj["s"]!.GetValue<string>());

        Assert.Throws<ArgumentException>(() => Mini.JsonValue.Create(double.NaN));
        Assert.Throws<ArgumentException>(() => Mini.JsonValue.Create(double.PositiveInfinity));
        Assert.Equal("\"\\u0001\\u001f\"", Mini.JsonValue.Create("\u0001\u001f")!.ToJsonString());
    }

    [Fact]
    public void Serializer_round_trips_bridge_messages()
    {
        var request = new BridgeRequest
        {
            Id = "abc",
            Token = "T0K3N",
            Method = "echo",
        };
        string json = Mini.MiniJsonSerializer.Serialize(request);
        Assert.Equal("{\"id\":\"abc\",\"token\":\"T0K3N\",\"method\":\"echo\"}", json); // null Params omitted, camelCase

        var info = new InstanceInfo
        {
            InstanceId = "revit-2023-42",
            Product = "revit",
            ProcessId = 42,
            ProcessStartTicksUtc = 638_000_000_000_000_000,
            StartedUtc = new DateTimeOffset(2026, 11, 6, 10, 30, 15, 123, TimeSpan.Zero),
        };
        var back = Mini.MiniJsonSerializer.Deserialize<InstanceInfo>(Mini.MiniJsonSerializer.Serialize(info, indented: true))!;
        Assert.Equal(info.InstanceId, back.InstanceId);
        Assert.Equal(info.ProcessId, back.ProcessId);
        Assert.Equal(info.ProcessStartTicksUtc, back.ProcessStartTicksUtc);
        Assert.Equal(info.StartedUtc, back.StartedUtc);

        var config = Mini.MiniJsonSerializer.Deserialize<BridgeConfig>("{\"allowCodeExecution\":false,\"mainThreadTimeoutSeconds\":30,\"unknown\":1}")!;
        Assert.False(config.AllowCodeExecution);
        Assert.True(config.AllowModifications); // default kept
        Assert.Equal(30, config.MainThreadTimeoutSeconds);
    }

#if !NETFRAMEWORK
    // ---------------------------------------------------------------- wire compatibility with System.Text.Json

    private static bool StjEquals(string a, string b) =>
        System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(a), System.Text.Json.Nodes.JsonNode.Parse(b));

    [Fact]
    public void Random_documents_round_trip_identically_through_both_libraries()
    {
        var random = new Random(20261106);
        for (int i = 0; i < 500; i++)
        {
            string original = RandomNode(random, 0)?.ToJsonString() ?? "null";

            // System.Text.Json -> MiniJson -> System.Text.Json must not change the document.
            string viaMini = Mini.JsonNode.Parse(original)?.ToJsonString() ?? "null";
            Assert.True(StjEquals(original, viaMini), $"Mismatch for document #{i}:\n{original}\n{viaMini}");

            // MiniJson's indented output is also valid for System.Text.Json.
            string indented = Mini.JsonNode.Parse(original)?.ToJsonString(indented: true) ?? "null";
            Assert.True(StjEquals(original, indented));
        }
    }

    private static System.Text.Json.Nodes.JsonNode? RandomNode(Random r, int depth)
    {
        int kind = depth > 3 ? r.Next(5) : r.Next(7);
        switch (kind)
        {
            case 0: return null;
            case 1: return System.Text.Json.Nodes.JsonValue.Create(r.Next(2) == 0);
            case 2: return System.Text.Json.Nodes.JsonValue.Create((long)(r.NextDouble() * long.MaxValue) * (r.Next(2) == 0 ? 1 : -1));
            case 3: return System.Text.Json.Nodes.JsonValue.Create(Math.Round((r.NextDouble() - 0.5) * Math.Pow(10, r.Next(-5, 25)), r.Next(0, 10)));
            case 4: return System.Text.Json.Nodes.JsonValue.Create(RandomString(r));
            case 5:
                var obj = new System.Text.Json.Nodes.JsonObject();
                for (int i = r.Next(6); i > 0; i--) obj[RandomString(r) + i.ToString(CultureInfo.InvariantCulture)] = RandomNode(r, depth + 1);
                return obj;
            default:
                var arr = new System.Text.Json.Nodes.JsonArray();
                for (int i = r.Next(6); i > 0; i--) arr.Add(RandomNode(r, depth + 1));
                return arr;
        }
    }

    private static string RandomString(Random r)
    {
        const string alphabet = "abcXYZ019 \"\\/\n\r\t\b\f\u0001\u001féüمرحبا✓😀";
        var chars = new System.Text.StringBuilder();
        for (int i = r.Next(12); i > 0; i--)
        {
            int idx = r.Next(alphabet.Length);
            if (char.IsHighSurrogate(alphabet[idx]) && idx + 1 < alphabet.Length) { chars.Append(alphabet, idx, 2); continue; }
            if (char.IsLowSurrogate(alphabet[idx])) continue;
            chars.Append(alphabet[idx]);
        }
        return chars.ToString();
    }

    [Fact]
    public void Bridge_messages_are_interchangeable_between_libraries()
    {
        // Request written by the .NET 10 server (System.Text.Json), read by a 2022-2024 add-in (MiniJson).
        var stjRequest = new BridgeRequest
        {
            Id = "1",
            Token = "tok",
            Method = "set_parameters",
            Params = new System.Text.Json.Nodes.JsonObject { ["updates"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["elementId"] = 123456, ["value"] = "3000 mm" }) },
        };
        string fromServer = BridgeJson.Serialize(stjRequest);
        var miniParsed = (Mini.JsonObject)Mini.JsonNode.Parse(fromServer)!;
        Assert.Equal("set_parameters", miniParsed["method"]!.GetValue<string>());
        Assert.Equal(123456L, miniParsed["params"]!["updates"]![0]!["elementId"]!.GetValue<long>());

        // Response written by a 2022-2024 add-in (MiniJson), read by the server (System.Text.Json).
        var miniResponse = new Mini.JsonObject
        {
            ["id"] = "1",
            ["ok"] = true,
            ["result"] = new Mini.JsonObject { ["walls"] = 12, ["name"] = "Level 1 – مرحبا" },
        };
        var stjResponse = BridgeJson.Deserialize<BridgeResponse>(miniResponse.ToJsonString())!;
        Assert.True(stjResponse.Ok);
        Assert.Equal(12, stjResponse.Result!["walls"]!.GetValue<int>());
        Assert.Equal("Level 1 – مرحبا", stjResponse.Result["name"]!.GetValue<string>());

        // Instance file written by a 2022-2024 add-in, read by the server.
        var info = new InstanceInfo { InstanceId = "autocad-2023-7", Product = "autocad", StartedUtc = DateTimeOffset.UtcNow, ProcessStartTicksUtc = 1 };
        var read = BridgeJson.Deserialize<InstanceInfo>(Mini.MiniJsonSerializer.Serialize(info, indented: true))!;
        Assert.Equal(info.InstanceId, read.InstanceId);
        Assert.Equal(info.StartedUtc, read.StartedUtc);
    }
#endif
}
