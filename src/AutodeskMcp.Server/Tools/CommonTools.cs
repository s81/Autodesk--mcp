using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AutodeskMcp.Bridge;
using ModelContextProtocol.Server;

namespace AutodeskMcp.Server.Tools;

[McpServerToolType]
public sealed class CommonTools(HostGateway gateway)
{
    [McpServerTool(Name = "list_autodesk_instances", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists running Revit and AutoCAD sessions on this computer that have the Autodesk MCP add-in loaded, " +
                 "with their instanceId, product year, and whether modifications / code execution are allowed. " +
                 "Pass instanceId to other tools only when more than one session of the same product is running. " +
                 "When installed from the .mcpb bundle, 'addinSetup' reports which add-ins were installed at startup; " +
                 "if a program is missing from 'instances', tell the user to restart it.")]
    public async Task<string> ListInstances(CancellationToken ct)
    {
        var result = new JsonArray();
        foreach (var info in InstanceRegistry.ListLive())
        {
            var entry = new JsonObject
            {
                ["instanceId"] = info.InstanceId,
                ["product"] = info.Product,
                ["productVersion"] = info.ProductVersion,
                ["productBuild"] = info.ProductBuild,
                ["processId"] = info.ProcessId,
                ["startedUtc"] = info.StartedUtc.ToString("o"),
            };
            try
            {
                var ping = await gateway.CallNodeAsync(info, "__ping", null, ct) as JsonObject;
                entry["reachable"] = true;
                entry["allowModifications"] = ping?["allowModifications"]?.DeepClone();
                entry["allowCodeExecution"] = ping?["allowCodeExecution"]?.DeepClone();
            }
            catch (System.Exception ex)
            {
                entry["reachable"] = false;
                entry["error"] = ex.Message;
            }
            result.Add(entry);
        }

        var response = new JsonObject
        {
            ["registryFolder"] = InstanceRegistry.InstancesDirectory,
            ["instances"] = result,
        };
        // Set when the server runs from the .mcpb bundle: which add-ins it installed at startup.
        if (AddinSetup.Last is { } setup) response["addinSetup"] = setup.ToJson();
        return response.ToJsonString(BridgeJson.Indented);
    }
}
