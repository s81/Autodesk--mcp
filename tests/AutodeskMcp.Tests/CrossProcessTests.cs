#if !NETFRAMEWORK
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AutodeskMcp.Bridge;
using AutodeskMcp.Server;
using Xunit;

namespace AutodeskMcp.Tests;

/// <summary>
/// End-to-end tests against a host running in a separate process (tests/AutodeskMcp.FakeHost):
///   net48   = how the Revit/AutoCAD 2022-2024 add-ins run (.NET Framework, MiniJson, ACL pipe) - Windows only
///   net10.0 = how the 2025-2027 add-ins run (System.Text.Json, CurrentUserOnly pipe)
/// The client side is always the real .NET 10 code used by the MCP server.
/// </summary>
[Collection("bridge")]
public sealed class CrossProcessTests
{
    public static TheoryData<string> HostRuntimes => new() { "net10.0", "net48" };

    private sealed class HostProcess : IDisposable
    {
        public Process Process { get; }
        public InstanceInfo Info { get; }

        public HostProcess(string runtime, string product)
        {
            // tests/AutodeskMcp.Tests/bin/<Configuration>/net10.0/  ->  tests/AutodeskMcp.FakeHost/bin/<Configuration>/<runtime>/
            var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string configuration = testBin.Parent!.Name;
            string hostDir = Path.Combine(testBin.Parent!.Parent!.Parent!.Parent!.FullName, "AutodeskMcp.FakeHost", "bin", configuration, runtime);
            string instanceId = $"{product}-xproc-{runtime}-{Guid.NewGuid():N}";

            ProcessStartInfo psi = runtime == "net48"
                ? new ProcessStartInfo(Path.Combine(hostDir, "AutodeskMcp.FakeHost.exe"))
                : new ProcessStartInfo("dotnet") { ArgumentList = { Path.Combine(hostDir, "AutodeskMcp.FakeHost.dll") } };
            psi.ArgumentList.Add(product);
            psi.ArgumentList.Add(instanceId);
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.Environment["AUTODESK_MCP_HOME"] = TestEnvironment.Home;

            Assert.True(File.Exists(psi.FileName == "dotnet" ? psi.ArgumentList[0] : psi.FileName),
                $"Fake host not built at {hostDir}. Build the solution (or tests/AutodeskMcp.FakeHost) first.");
            Process = Process.Start(psi)!;
            _ = Process.StandardError.ReadToEndAsync();

            string? ready = Process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
            Assert.Equal("READY " + instanceId, ready);
            Info = InstanceRegistry.Resolve(product, instanceId);
        }

        public void Dispose()
        {
            try { Process.StandardInput.Close(); } catch { }
            if (!Process.WaitForExit(10_000)) { try { Process.Kill(true); } catch { } }
            Process.Dispose();
        }
    }

    private static void SkipUnlessAvailable(string runtime) =>
        Assert.SkipWhen(runtime == "net48" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            ".NET Framework 4.8 hosts only run on Windows (covered by the windows-latest CI job).");

    [Theory]
    [MemberData(nameof(HostRuntimes))]
    public async Task Client_talks_to_host_process(string runtime)
    {
        SkipUnlessAvailable(runtime);
        using var host = new HostProcess(runtime, "revit");

        var info = (JsonObject)(await BridgeClient.CallAsync(host.Info, "session_info", null, TimeSpan.FromSeconds(30)))!;
        Assert.True(info["fake"]!.GetValue<bool>());
        Assert.True(info["onMainThread"]!.GetValue<bool>());
        Assert.Equal("مرحبا \"quoted\" \\ ✓", info["unicode"]!.GetValue<string>());
        if (runtime == "net48") Assert.StartsWith(".NET Framework 4.8", info["runtime"]!.GetValue<string>());

        var payload = new JsonObject { ["n"] = 9007199254740993L, ["nested"] = new JsonArray(1.5, "x", null, new JsonObject()) };
        var echo = await BridgeClient.CallAsync(host.Info, "echo", payload, TimeSpan.FromSeconds(30));
        Assert.True(JsonNode.DeepEquals(payload, echo), echo?.ToJsonString());

        var badToken = new InstanceInfo { PipeName = host.Info.PipeName, Token = "wrong", Product = "revit" };
        var ex = await Assert.ThrowsAsync<BridgeException>(() => BridgeClient.CallAsync(badToken, "session_info", null, TimeSpan.FromSeconds(30)));
        Assert.Contains("Authentication failed", ex.Message);
    }

    [Theory]
    [MemberData(nameof(HostRuntimes))]
    public async Task Script_compiled_by_server_runs_in_host_process(string runtime)
    {
        SkipUnlessAvailable(runtime);
        using var host = new HostProcess(runtime, "autocad");

        // Same flow as *_execute_csharp: get the host's assemblies, compile with Roslyn here, run there.
        var refs = (JsonArray)(await BridgeClient.CallAsync(host.Info, "__script_references", null, TimeSpan.FromSeconds(30)))!;
        var paths = refs.Select(n => n!.GetValue<string>()).ToList();
        if (runtime == "net48") Assert.Contains(paths, p => p.EndsWith("mscorlib.dll", StringComparison.OrdinalIgnoreCase));

        var compiled = ScriptCompiler.Compile(
            ScriptCompiler.Wrap(new[] { "System", "System.Linq", "System.Collections.Generic" }, null,
                "System.Text.StringBuilder output", "",
                "output.Append(\"ran on \" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);\n" +
                "return new { total = Enumerable.Range(1, 10).Sum(), items = new List<string> { \"a\", \"b\" } };"),
            ScriptCompiler.CreateReferences(paths));
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));

        var result = await BridgeClient.CallAsync(host.Info, "execute_script",
            new JsonObject { ["assembly"] = Convert.ToBase64String(compiled.Assembly!) }, TimeSpan.FromSeconds(60));
        Assert.Equal(55, result!["result"]!["total"]!.GetValue<int>());
        Assert.Equal(2, result["result"]!["items"]!.AsArray().Count);
        string output = result["output"]!.GetValue<string>();
        Assert.StartsWith(runtime == "net48" ? "ran on .NET Framework 4.8" : "ran on .NET 10", output);
    }

    [Theory]
    [MemberData(nameof(HostRuntimes))]
    public async Task Mcp_server_reaches_host_process(string runtime)
    {
        SkipUnlessAvailable(runtime);
        using var host = new HostProcess(runtime, "revit");
        string serverDll = Path.Combine(AppContext.BaseDirectory, "AutodeskMcp.Server.dll");
        var psi = new ProcessStartInfo("dotnet") { ArgumentList = { serverDll } };
        psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.Environment["AUTODESK_MCP_HOME"] = TestEnvironment.Home;
        using var server = Process.Start(psi)!;
        _ = server.StandardError.ReadToEndAsync();

        async Task<JsonObject> Rpc(int id, string method, JsonObject? p = null)
        {
            var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = p ?? new JsonObject() };
            await server.StandardInput.WriteLineAsync(msg.ToJsonString());
            await server.StandardInput.FlushAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (true)
            {
                string? line = await server.StandardOutput.ReadLineAsync(cts.Token);
                Assert.NotNull(line);
                var node = JsonNode.Parse(line!)!.AsObject();
                if (node["id"]?.GetValue<int>() == id) return node;
            }
        }

        try
        {
            await Rpc(1, "initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "1" },
            });
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

            var list = await Rpc(2, "tools/call", new JsonObject { ["name"] = "list_autodesk_instances", ["arguments"] = new JsonObject() });
            var instances = JsonNode.Parse(list["result"]!["content"]![0]!["text"]!.GetValue<string>())!["instances"]!.AsArray();
            var mine = instances.Single(i => i!["instanceId"]!.GetValue<string>() == host.Info.InstanceId)!;
            Assert.True(mine["reachable"]!.GetValue<bool>(), mine.ToJsonString());

            var call = await Rpc(3, "tools/call", new JsonObject
            {
                ["name"] = "revit_get_session_info",
                ["arguments"] = new JsonObject { ["instanceId"] = host.Info.InstanceId },
            });
            string text = call["result"]!["content"]![0]!["text"]!.GetValue<string>();
            Assert.True(JsonNode.Parse(text)!["fake"]!.GetValue<bool>(), text);
        }
        finally
        {
            try { server.Kill(true); } catch { }
        }
    }
}
#endif
