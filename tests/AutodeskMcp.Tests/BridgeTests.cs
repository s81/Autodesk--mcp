using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using AutodeskMcp.Bridge;
using Xunit;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
using AutodeskMcp.Server;
#endif

namespace AutodeskMcp.Tests;

internal static class TestEnvironment
{
    public static readonly string Home = Path.Combine(Path.GetTempPath(), "amcp-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));

    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("AUTODESK_MCP_HOME", Home);
}

// One collection: the tests share the registry folder, and some count live instances.
[Collection("bridge")]
public sealed class BridgeTests
{
    [Fact]
    public async Task Framing_round_trips_large_unicode_payload()
    {
        string payload = new string('x', 3_000_000) + " مرحبا ✓";
        using var ms = new MemoryStream();
        await Framing.WriteAsync(ms, payload, default);
        ms.Position = 0;
        Assert.Equal(payload, await Framing.ReadAsync(ms, default));
        Assert.Null(await Framing.ReadAsync(ms, default)); // clean EOF
    }

    [Fact]
    public async Task Call_runs_on_main_thread_and_round_trips()
    {
        using var host = new FakeHost("revit");
        var info = (JsonObject)(await host.Call("session_info"))!;
        Assert.True(info["onMainThread"]!.GetValue<bool>());

        var echo = await host.Call("echo", new JsonObject { ["a"] = 1, ["b"] = "two" });
        Assert.Equal("two", echo!["b"]!.GetValue<string>());
    }

    [Fact]
    public async Task Wrong_token_is_rejected()
    {
        using var host = new FakeHost("revit");
        var ex = await Assert.ThrowsAsync<BridgeException>(() => host.Call("session_info", token: "not-the-token"));
        Assert.Contains("Authentication failed", ex.Message);
    }

    [Fact]
    public async Task Handler_errors_are_reported_and_unknown_methods_fail()
    {
        using var host = new FakeHost("autocad");
        Assert.Equal("kaboom", (await Assert.ThrowsAsync<BridgeException>(() => host.Call("boom"))).Message);
        Assert.Contains("Unknown method", (await Assert.ThrowsAsync<BridgeException>(() => host.Call("nope"))).Message);
    }

    [Fact]
    public async Task Config_blocks_modifications_and_code_execution()
    {
        using var host = new FakeHost("revit", new BridgeConfig { AllowModifications = false, AllowCodeExecution = false });
        var ex = await Assert.ThrowsAsync<BridgeException>(() => host.Call("modify"));
        Assert.Contains("modifications are disabled", ex.Message);
        Assert.Equal(0, host.ModifyCalls);

        var ping = (JsonObject)(await host.Call("__ping"))!;
        Assert.False(ping["allowCodeExecution"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Timed_out_work_never_runs_later()
    {
        using var host = new FakeHost("revit", new BridgeConfig { MainThreadTimeoutSeconds = 1 });
        host.Dispatcher.Paused = true; // simulate a modal dialog
        var ex = await Assert.ThrowsAsync<BridgeException>(() => host.Call("modify"));
        Assert.Contains("not executed", ex.Message);

        host.Dispatcher.Paused = false; // dialog closed
        await host.Call("session_info"); // drain the queue past the cancelled item
        Assert.Equal(0, host.ModifyCalls);
    }

    [Fact]
    public void Registry_resolves_single_instance_and_requires_id_for_several()
    {
        using var a = new FakeHost("autocad");
        Assert.Equal(a.Info.InstanceId, InstanceRegistry.Resolve("autocad", null).InstanceId);

        using var b = new FakeHost("autocad");
        Assert.Contains("pass instanceId", Assert.Throws<BridgeException>(() => InstanceRegistry.Resolve("autocad", null)).Message);
        Assert.Equal(b.Info.InstanceId, InstanceRegistry.Resolve("autocad", b.Info.InstanceId).InstanceId);
    }

    [Fact]
    public void Registry_prunes_entries_of_dead_processes()
    {
        var stale = new InstanceInfo { InstanceId = "revit-2099-dead", Product = "revit", ProcessId = int.MaxValue - 7, PipeName = "x", Token = "t" };
        string path = InstanceRegistry.Register(stale);
        Assert.DoesNotContain(InstanceRegistry.ListLive("revit"), i => i.InstanceId == stale.InstanceId);
        Assert.False(File.Exists(path));
    }

    // ------------------------------------------------------------- scripts
#if NETFRAMEWORK
    /// <summary>
    /// On .NET Framework the test cannot reference the .NET 10 server, so it compiles the script with the
    /// in-box CodeDom compiler instead. What is under test is the host side: Assembly.Load on .NET Framework,
    /// invocation, exception unwrapping and MiniJson conversion of the result.
    /// </summary>
    private static byte[] CompileWithCodeDom(string body)
    {
        string dir = Path.Combine(TestEnvironment.Home, "codedom");
        Directory.CreateDirectory(dir);
        string output = Path.Combine(dir, ScriptRunner.AssemblyNamePrefix + Guid.NewGuid().ToString("N") + ".dll");
        string source = "using System; using System.Linq; using System.Collections.Generic;\n" +
                        "public static class McpScript { public static object Run(System.Text.StringBuilder output) {\n" +
                        body + "\n} }";
        using var provider = new Microsoft.CSharp.CSharpCodeProvider();
        var options = new System.CodeDom.Compiler.CompilerParameters { OutputAssembly = output, GenerateInMemory = false };
        options.ReferencedAssemblies.AddRange(new[] { "System.dll", "System.Core.dll" });
        var results = provider.CompileAssemblyFromSource(options, source);
        Assert.False(results.Errors.HasErrors, string.Join("\n", results.Errors.Cast<System.CodeDom.Compiler.CompilerError>()));
        return File.ReadAllBytes(output);
    }

    [Fact]
    public async Task Script_runs_in_net48_host()
    {
        byte[] asm = CompileWithCodeDom(
            "output.AppendLine(\"hello from script\"); var squares = Enumerable.Range(1, 4).Select(i => i * i).ToList(); " +
            "return new { sum = squares.Sum(), squares = squares };");
        using var host = new FakeHost("revit");
        var result = await host.Call("execute_script", new JsonObject { ["assembly"] = Convert.ToBase64String(asm) });
        Assert.Equal(30, result!["result"]!["sum"]!.GetValue<int>());
        Assert.Equal(4, result["result"]!["squares"]!.AsArray().Count);
        Assert.Contains("hello from script", result["output"]!.GetValue<string>());
    }

    [Fact]
    public async Task Script_exception_is_unwrapped_in_net48_host()
    {
        byte[] asm = CompileWithCodeDom("var list = new List<int>(); return list[5];");
        using var host = new FakeHost("revit");
        var ex = await Assert.ThrowsAsync<BridgeException>(() =>
            host.Call("execute_script", new JsonObject { ["assembly"] = Convert.ToBase64String(asm) }));
        Assert.Contains("ArgumentOutOfRangeException", ex.Message);
    }
#else

    private static readonly IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> Refs =
        ScriptCompiler.CreateReferences(ScriptRunner.GetReferencePaths());

    private static CompileResult CompileGeneric(string code) => ScriptCompiler.Compile(
        ScriptCompiler.Wrap(new[] { "System", "System.Linq", "System.Collections.Generic" }, null,
            "System.Text.StringBuilder output", "", code),
        Refs);

    [Fact]
    public async Task Script_compiles_out_of_process_and_runs_in_host()
    {
        var compiled = CompileGeneric("""
            output.AppendLine("hello from script");
            var squares = Enumerable.Range(1, 4).Select(i => i * i).ToList();
            return new { sum = squares.Sum(), squares, tuple = (1, "a") };
            """);
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));

        using var host = new FakeHost("revit");
        var result = await host.Call("execute_script", new JsonObject { ["assembly"] = Convert.ToBase64String(compiled.Assembly!) });
        Assert.Equal(30, result!["result"]!["sum"]!.GetValue<int>());
        Assert.Equal(4, result["result"]!["squares"]!.AsArray().Count);
        Assert.Contains("hello from script", result["output"]!.GetValue<string>());
    }

    [Fact]
    public void Script_compile_errors_point_at_user_lines()
    {
        var compiled = CompileGeneric("var x = 1;\nint y = \"not an int\";\nreturn x;");
        Assert.False(compiled.Success);
        Assert.Contains(compiled.Errors, e => e.Contains("line 2"));
    }

    [Fact]
    public async Task Script_runtime_exception_reports_script_line()
    {
        var compiled = CompileGeneric("var list = new List<int>();\nreturn list[5];");
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));
        using var host = new FakeHost("revit");
        var ex = await Assert.ThrowsAsync<BridgeException>(() =>
            host.Call("execute_script", new JsonObject { ["assembly"] = Convert.ToBase64String(compiled.Assembly!) }));
        Assert.Contains("ArgumentOutOfRangeException", ex.Message);
        Assert.Contains("script.cs:line 2", ex.Message);
    }

    [Fact]
    public void Using_directives_cannot_inject_code()
    {
        Assert.ThrowsAny<Exception>(() => ScriptCompiler.WrapRevit("return 1;", new[] { "System; } class X { " }));
    }

#endif

    [Fact]
    public void SafeJson_bounds_depth_and_size()
    {
        var node = SafeJson.ToNode(Enumerable.Range(0, 5000), maxItems: 10)!.AsArray();
        Assert.Equal(11, node.Count); // 10 + truncation marker
        Assert.Equal("NaN", SafeJson.ToNode(double.NaN)!.GetValue<string>());
    }

    // ------------------------------------------------------------- MCP server over stdio
#if !NETFRAMEWORK

    [Fact]
    public async Task Mcp_server_lists_tools_and_proxies_a_call_to_the_host()
    {
        using var revit = new FakeHost("revit");
        string serverDll = Path.Combine(AppContext.BaseDirectory, "AutodeskMcp.Server.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{serverDll}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["AUTODESK_MCP_HOME"] = TestEnvironment.Home;
        using var proc = Process.Start(psi)!;
        _ = proc.StandardError.ReadToEndAsync(); // drain logs

        async Task<JsonObject> Rpc(int id, string method, JsonObject? p = null)
        {
            var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = p ?? new JsonObject() };
            await proc.StandardInput.WriteLineAsync(msg.ToJsonString());
            await proc.StandardInput.FlushAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (true)
            {
                string? line = await proc.StandardOutput.ReadLineAsync(cts.Token);
                Assert.NotNull(line);
                var node = JsonNode.Parse(line!)!.AsObject();
                if (node["id"]?.GetValue<int>() == id) return node;
            }
        }

        try
        {
            var init = await Rpc(1, "initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "1" },
            });
            Assert.Equal("autodesk-mcp", init["result"]!["serverInfo"]!["name"]!.GetValue<string>());
            await proc.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

            var tools = (await Rpc(2, "tools/list"))["result"]!["tools"]!.AsArray()
                .Select(t => t!["name"]!.GetValue<string>()).ToHashSet();
            foreach (string expected in new[]
                     {
                         "list_autodesk_instances", "revit_get_session_info", "revit_list_elements", "revit_set_parameters",
                         "revit_create_walls", "revit_execute_csharp", "autocad_get_session_info", "autocad_create_entities",
                         "autocad_modify_entities", "autocad_execute_csharp",
                     })
                Assert.Contains(expected, tools);

            var call = await Rpc(3, "tools/call", new JsonObject
            {
                ["name"] = "revit_get_session_info",
                ["arguments"] = new JsonObject { ["instanceId"] = revit.Info.InstanceId },
            });
            string text = call["result"]!["content"]![0]!["text"]!.GetValue<string>();
            Assert.True(JsonNode.Parse(text)!["fake"]!.GetValue<bool>(), text);

            // A missing AutoCAD instance surfaces as a readable tool error, not a crash.
            var missing = await Rpc(4, "tools/call", new JsonObject { ["name"] = "autocad_get_session_info", ["arguments"] = new JsonObject() });
            Assert.True(missing["result"]!["isError"]!.GetValue<bool>());
            Assert.Contains("No running autocad instance", missing["result"]!["content"]![0]!["text"]!.GetValue<string>());

            Console.WriteLine($"MCP server exposes {tools.Count} tools: {string.Join(", ", tools.Order())}");
        }
        finally
        {
            try { proc.Kill(true); } catch { }
        }
    }
#endif
}
