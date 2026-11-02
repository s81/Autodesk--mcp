using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AutodeskMcp.Server.Tools;

public sealed class ParameterUpdate
{
    [Description("Element id (integer).")]
    public long ElementId { get; set; }

    [Description("Parameter name as shown in Revit's Properties palette, or a BuiltInParameter name such as 'ALL_MODEL_MARK'.")]
    public string Parameter { get; set; } = "";

    [Description("New value as text. With mode 'display' numbers are read like typing into the Properties palette, " +
                 "using the project's units (e.g. '3000' or '3000 mm'). For element-id parameters pass the id.")]
    public string Value { get; set; } = "";

    [Description("'display' (default): parse like the UI, in project units. 'internal': value is a number in Revit internal units (feet, radians, ...).")]
    public string? Mode { get; set; }
}

public sealed class WallSpec
{
    [Description("Start point [x, y] in the tool's 'units'.")]
    public double[] Start { get; set; } = Array.Empty<double>();

    [Description("End point [x, y] in the tool's 'units'.")]
    public double[] End { get; set; } = Array.Empty<double>();
}

[McpServerToolType]
public sealed class RevitTools(HostGateway gateway, ScriptCompiler compiler)
{
    private const string InstanceDesc = "Optional Revit instanceId from list_autodesk_instances; needed only when several Revit sessions are running.";

    private Task<string> Call(string? instanceId, string method, object? p, CancellationToken ct) =>
        gateway.CallAsync(HostGateway.Revit, instanceId, method, p, ct);

    [McpServerTool(Name = "revit_get_session_info", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Revit version, open documents, active document and view, project length units, and selection count.")]
    public Task<string> SessionInfo([Description(InstanceDesc)] string? instanceId = null, CancellationToken ct = default)
        => Call(instanceId, "session_info", null, ct);

    [McpServerTool(Name = "revit_list_elements", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Finds elements in the active document. Filters combine with AND. Returns id, name, category, " +
                 "type, level and location (feet). Use revit_get_elements for parameters.")]
    public Task<string> ListElements(
        [Description("Category: BuiltInCategory name (e.g. 'OST_Walls') or the category name in the document's language (e.g. 'Walls').")] string? category = null,
        [Description("Revit API class name, e.g. 'Wall', 'FamilyInstance', 'Level', 'ViewSheet'.")] string? className = null,
        [Description("Only elements visible in this view id.")] long? viewId = null,
        [Description("true: element types (wall types, family symbols); false (default): placed instances.")] bool typesOnly = false,
        [Description("Case-insensitive substring of the element name.")] string? nameContains = null,
        [Description("Max results (default 200, max 5000).")] int limit = 200,
        [Description("Results to skip, for paging.")] int offset = 0,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "list_elements", new { category, className, viewId, typesOnly, nameContains, limit, offset }, ct);

    [McpServerTool(Name = "revit_get_elements", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Full details of elements by id: category, type, level, location, bounding box and parameters " +
                 "(value as shown in the UI plus storage type and raw value).")]
    public Task<string> GetElements(
        [Description("Element ids.")] long[] elementIds,
        [Description("Include instance parameters (default true).")] bool includeParameters = true,
        [Description("Also include the element type's parameters (default false).")] bool includeTypeParameters = false,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "get_elements", new { elementIds, includeParameters, includeTypeParameters }, ct);

    [McpServerTool(Name = "revit_get_selection", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Ids and summaries of the elements currently selected in the Revit UI.")]
    public Task<string> GetSelection([Description(InstanceDesc)] string? instanceId = null, CancellationToken ct = default)
        => Call(instanceId, "get_selection", null, ct);

    [McpServerTool(Name = "revit_set_selection", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Selects the given elements in the Revit UI (does not change the model). Pass an empty list to clear.")]
    public Task<string> SetSelection(
        [Description("Element ids to select.")] long[] elementIds,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "set_selection", new { elementIds }, ct);

    [McpServerTool(Name = "revit_list_views", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists views and sheets (templates excluded) with id, name, view type, level and sheet number.")]
    public Task<string> ListViews(
        [Description("Optional ViewType filter, e.g. 'FloorPlan', 'Section', 'ThreeD', 'DrawingSheet', 'Schedule'.")] string? viewType = null,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "list_views", new { viewType }, ct);

    [McpServerTool(Name = "revit_list_levels", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists levels with id, name and elevation (feet and project display units).")]
    public Task<string> ListLevels([Description(InstanceDesc)] string? instanceId = null, CancellationToken ct = default)
        => Call(instanceId, "list_levels", null, ct);

    [McpServerTool(Name = "revit_get_schedule_data", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Reads a schedule's header and body cells as text rows, exactly as displayed in Revit.")]
    public Task<string> GetScheduleData(
        [Description("Schedule view id. Either this or scheduleName is required.")] long? scheduleId = null,
        [Description("Exact schedule name.")] string? scheduleName = null,
        [Description("Max body rows (default 1000).")] int maxRows = 1000,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "get_schedule_data", new { scheduleId, scheduleName, maxRows }, ct);

    [McpServerTool(Name = "revit_set_parameters", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Sets parameter values on elements in one undoable transaction. All updates succeed or none are applied.")]
    public Task<string> SetParameters(
        [Description("Updates to apply.")] List<ParameterUpdate> updates,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "set_parameters", new { updates }, ct);

    [McpServerTool(Name = "revit_create_walls", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates straight walls on a level in one undoable transaction. Returns the new element ids.")]
    public Task<string> CreateWalls(
        [Description("Walls to create.")] List<WallSpec> walls,
        [Description("Length unit of all coordinates and height: 'mm', 'cm', 'm', 'ft' or 'in'.")] string units,
        [Description("Base level id (see revit_list_levels).")] long levelId,
        [Description("Unconnected wall height in 'units'.")] double height,
        [Description("Wall type id (see revit_list_elements category 'OST_Walls' typesOnly=true). Defaults to the document's default wall type.")] long? wallTypeId = null,
        [Description("Create as structural walls (default false).")] bool structural = false,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "create_walls", new { walls, units, levelId, height, wallTypeId, structural }, ct);

    [McpServerTool(Name = "revit_delete_elements", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Deletes elements in one undoable transaction. Revit also deletes dependents (e.g. hosted doors); " +
                 "all deleted ids are returned.")]
    public Task<string> DeleteElements(
        [Description("Element ids to delete.")] long[] elementIds,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
        => Call(instanceId, "delete_elements", new { elementIds }, ct);

    [McpServerTool(Name = "revit_execute_csharp", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Runs a C# method body inside Revit on the API thread, for anything the other tools cannot do. " +
        "Variables in scope: uiapp (UIApplication), app (Application), uidoc (UIDocument, may be null), " +
        "doc (Document, may be null), output (StringBuilder for log text). Namespaces imported: System, " +
        "System.Collections.Generic, System.Linq, System.Text, Autodesk.Revit.DB, Autodesk.Revit.UI. " +
        "Use 'return <value>;' to return data (primitives, collections, anonymous objects, Elements, XYZ...). " +
        "If transactionName is given the code runs inside that transaction (committed on success, rolled back on " +
        "exception) and must not start its own Transaction. Omit it for read-only code or when managing transactions yourself. " +
        "Revit internal units are feet and radians; convert with UnitUtils.")]
    public async Task<string> ExecuteCSharp(
        [Description("C# statements forming a method body.")] string code,
        [Description("Wrap the code in a transaction with this name (shown in Undo).")] string? transactionName = null,
        [Description("Additional using directives, e.g. 'Autodesk.Revit.DB.Structure'.")] string[]? usings = null,
        [Description(InstanceDesc)] string? instanceId = null,
        CancellationToken ct = default)
    {
        var instance = gateway.Resolve(HostGateway.Revit, instanceId);
        var compiled = await compiler.CompileForAsync(instance, ScriptCompiler.WrapRevit(code, usings), ct);
        if (!compiled.Success) throw new McpException(ScriptCompiler.FormatErrors(compiled));

        var result = await gateway.CallNodeAsync(instance, "execute_script", new JsonObject
        {
            ["assembly"] = Convert.ToBase64String(compiled.Assembly!),
            ["transactionName"] = transactionName,
        }, ct);
        return result?.ToJsonString(Bridge.BridgeJson.Indented) ?? "null";
    }
}
