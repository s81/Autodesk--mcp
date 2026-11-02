using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutodeskMcp.Server.Tools;

public sealed class EntitySpec
{
    [Description("'line' (points[0..1]), 'polyline' (points, closed), 'circle' (center, radius), " +
                 "'arc' (center, radius, startAngle, endAngle in degrees, counter-clockwise), 'point' (points[0]), " +
                 "'text' (points[0] insertion, text, height, rotation), 'mtext' (points[0], text, height, width).")]
    public string Type { get; set; } = "";

    [Description("Points as [x, y] or [x, y, z] in drawing units (WCS).")]
    public List<double[]>? Points { get; set; }

    [Description("Center [x, y] or [x, y, z] for circle/arc.")]
    public double[]? Center { get; set; }

    public double? Radius { get; set; }

    [Description("Arc start angle in degrees.")]
    public double? StartAngle { get; set; }

    [Description("Arc end angle in degrees.")]
    public double? EndAngle { get; set; }

    [Description("Close the polyline.")]
    public bool? Closed { get; set; }

    [Description("Text content for text/mtext.")]
    public string? Text { get; set; }

    [Description("Text height in drawing units (defaults to the current TEXTSIZE).")]
    public double? Height { get; set; }

    [Description("MText box width (0 = no wrapping).")]
    public double? Width { get; set; }

    [Description("Text rotation in degrees.")]
    public double? Rotation { get; set; }

    [Description("Layer name; must exist (create it with autocad_create_layer). Defaults to the current layer.")]
    public string? Layer { get; set; }

    [Description("ACI color 0-256 (256 = ByLayer, 0 = ByBlock). Defaults to ByLayer.")]
    public int? ColorIndex { get; set; }
}

[McpServerToolType]
public sealed class AutoCADTools(HostGateway gateway, ScriptCompiler compiler)
{
    private const string InstanceDesc = "Optional AutoCAD instanceId from list_autodesk_instances; needed only when several AutoCAD sessions are running.";
    private const string SpaceDesc = "'model' (default) or a paper-space layout name.";

    private Task<string> Call(string? instanceId, string method, object? p, CancellationToken ct) =>
        gateway.CallAsync(HostGateway.AutoCAD, instanceId, method, p, ct);

    [McpServerTool(Name = "autocad_get_session_info", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("AutoCAD version, open drawings, active drawing, units (INSUNITS/LUNITS), current layer, drawing extents and layouts.")]
    public Task<string> SessionInfo([Description(InstanceDesc)] string? instanceId = null, CancellationToken ct = default)
        => Call(instanceId, "session_info", null, ct);

    [McpServerTool(Name = "autocad_list_layers", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists layers with color, linetype, lineweight, on/off, frozen, locked and plottable state.")]
    public Task<string> ListLayers([Description(InstanceDesc)] string? instanceId = null, CancellationToken ct = default)
        => Call(instanceId, "list_layers", null, ct);

    [McpServerTool(Name = "autocad_create_layer", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Creates a layer, or updates it if it already exists. Only properties you pass are changed.")]
    public Task<string> CreateLayer(
        [Description("Layer name.")] string name,
        [Description("ACI color 1-255.")] int? colorIndex = null,
        [Description("Linetype name; must already be loaded in the drawing.")] string? linetype = null,
        [Description("Turn the layer off.")] bool? isOff = null,
        [Description("Freeze the layer.")] bool? isFrozen = null,
        [Description("Lock the layer.")] bool? isLocked = null,
        [Description("Make it the current layer.")] bool makeCurrent = false,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "create_layer", new { name, colorIndex, linetype, isOff, isFrozen, isLocked, makeCurrent }, ct);

    [McpServerTool(Name = "autocad_list_entities", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists entities in model space or a layout with handle, type, layer, color and key geometry.")]
    public Task<string> ListEntities(
        [Description(SpaceDesc)] string? space = null,
        [Description("DXF type names to include, e.g. ['LINE','LWPOLYLINE','INSERT','TEXT','MTEXT','CIRCLE','DIMENSION'].")] string[]? types = null,
        [Description("Only entities on this layer (case-insensitive).")] string? layer = null,
        [Description("Max results (default 500, max 10000).")] int limit = 500,
        [Description("Results to skip, for paging.")] int offset = 0,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "list_entities", new { space, types, layer, limit, offset }, ct);

    [McpServerTool(Name = "autocad_get_entities", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Full details of entities by handle, including geometry, extents, block attributes and dynamic block properties.")]
    public Task<string> GetEntities(
        [Description("Entity handles (hex strings, e.g. '2A3F').")] string[] handles,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "get_entities", new { handles }, ct);

    [McpServerTool(Name = "autocad_create_entities", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates lines, polylines, circles, arcs, points, text and mtext in one undoable operation. Returns new handles.")]
    public Task<string> CreateEntities(
        [Description("Entities to create.")] List<EntitySpec> entities,
        [Description(SpaceDesc)] string? space = null,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "create_entities", new { entities, space }, ct);

    [McpServerTool(Name = "autocad_modify_entities", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Changes layer/color, moves (translate), rotates, scales or deletes entities by handle, in one operation.")]
    public Task<string> ModifyEntities(
        [Description("Entity handles.")] string[] handles,
        [Description("New layer (must exist).")] string? layer = null,
        [Description("New ACI color 0-256.")] int? colorIndex = null,
        [Description("Displacement [dx, dy] or [dx, dy, dz] in drawing units.")] double[]? translate = null,
        [Description("Rotation in degrees about basePoint (Z axis).")] double? rotate = null,
        [Description("Uniform scale factor about basePoint.")] double? scale = null,
        [Description("Base point for rotate/scale; default [0,0,0].")] double[]? basePoint = null,
        [Description("Delete the entities (other options are ignored).")] bool delete = false,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "modify_entities", new { handles, layer, colorIndex, translate, rotate, scale, basePoint, delete }, ct);

    [McpServerTool(Name = "autocad_list_blocks", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists block definitions (excluding layouts and anonymous blocks unless requested) with attribute tags and reference counts.")]
    public Task<string> ListBlocks(
        [Description("Include anonymous (*U, *D...) blocks.")] bool includeAnonymous = false,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "list_blocks", new { includeAnonymous }, ct);

    [McpServerTool(Name = "autocad_insert_block", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Inserts a block reference of an existing block definition, filling attributes.")]
    public Task<string> InsertBlock(
        [Description("Block definition name.")] string blockName,
        [Description("Insertion point [x, y] or [x, y, z].")] double[] position,
        [Description("Uniform scale (default 1).")] double scale = 1,
        [Description("Rotation in degrees (default 0).")] double rotation = 0,
        [Description("Layer (must exist). Defaults to current layer.")] string? layer = null,
        [Description("Attribute values by tag.")] Dictionary<string, string>? attributes = null,
        [Description(SpaceDesc)] string? space = null,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "insert_block", new { blockName, position, scale, rotation, layer, attributes, space }, ct);

    [McpServerTool(Name = "autocad_send_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Queues a command-line string in the active drawing exactly as if typed (e.g. '_.ZOOM _E ', '_.REGEN '). " +
                 "It runs asynchronously after this tool returns, so no result is reported; inspect the drawing afterwards. " +
                 "End with a space or \\n to press Enter. Use English underscore-prefixed command names for language independence.")]
    public Task<string> SendCommand(
        [Description("Command string.")] string command,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "send_command", new { command }, ct);

    [McpServerTool(Name = "autocad_execute_csharp", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Runs a C# method body inside AutoCAD on the main thread with the active document locked, for anything the " +
        "other tools cannot do. Variables in scope: doc (Document), db (Database), ed (Editor), tr (an open " +
        "Transaction on db, committed on success when commit=true, aborted otherwise or on exception), output " +
        "(StringBuilder for log text). Namespaces imported: System, System.Collections.Generic, System.Linq, System.Text, " +
        "Autodesk.AutoCAD.ApplicationServices/DatabaseServices/EditorInput/Geometry. " +
        "Use 'return <value>;' to return data. Do not prompt the user (ed.GetXxx) - there is no interactive command context.")]
    public async Task<string> ExecuteCSharp(
        [Description("C# statements forming a method body.")] string code,
        [Description("Commit the transaction on success (default true). Use false for read-only code.")] bool commit = true,
        [Description("Additional using directives, e.g. 'Autodesk.AutoCAD.Colors'.")] string[]? usings = null,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
    {
        var instance = gateway.Resolve(HostGateway.AutoCAD, instanceId);
        var compiled = await compiler.CompileForAsync(instance, ScriptCompiler.WrapAutoCAD(code, usings), ct);
        if (!compiled.Success) throw new McpException(ScriptCompiler.FormatErrors(compiled));

        var result = await gateway.CallNodeAsync(instance, "execute_script", new JsonObject
        {
            ["assembly"] = Convert.ToBase64String(compiled.Assembly!),
            ["commit"] = commit,
        }, ct);
        return result?.ToJsonString(Bridge.BridgeJson.Indented) ?? "null";
    }
}
