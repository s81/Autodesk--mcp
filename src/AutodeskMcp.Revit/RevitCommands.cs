using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif
using AutodeskMcp.Bridge;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AutodeskMcp.Revit;

/// <summary>Bridge methods implemented against the Revit API. Every handler runs on Revit's API thread.</summary>
internal static class RevitCommands
{
    public static IEnumerable<BridgeCommand> Create(RevitDispatcher dispatcher) => new[]
    {
        new BridgeCommand("session_info", _ => SessionInfo()),
        new BridgeCommand("list_elements", ListElements),
        new BridgeCommand("get_elements", GetElements),
        new BridgeCommand("get_selection", _ => GetSelection()),
        new BridgeCommand("set_selection", SetSelection),
        new BridgeCommand("list_views", ListViews),
        new BridgeCommand("list_levels", _ => ListLevels()),
        new BridgeCommand("get_schedule_data", GetScheduleData),
        new BridgeCommand("set_parameters", SetParameters) { Modifies = true },
        new BridgeCommand("create_walls", CreateWalls) { Modifies = true },
        new BridgeCommand("delete_elements", DeleteElements) { Modifies = true },
        new BridgeCommand("execute_script", ExecuteScript) { Modifies = true, ExecutesCode = true },
    };

    private static UIApplication App => RevitDispatcher.Current;

    private static UIDocument UiDoc =>
        App.ActiveUIDocument ?? throw new BridgeException("No active Revit document. Open a project or family first.");

    private static Document Doc => UiDoc.Document;

    private static ElementId Id(long value) => RevitCompat.ToElementId(value);

    private static JsonArray Arr(IEnumerable<JsonNode?> items) => new(items.ToArray());

    // ---------------------------------------------------------------- read

    private static JsonNode SessionInfo()
    {
        var app = App.Application;
        var info = new JsonObject
        {
            ["versionNumber"] = app.VersionNumber,
            ["versionName"] = app.VersionName,
            ["versionBuild"] = app.VersionBuild,
            ["subVersionNumber"] = app.SubVersionNumber,
            ["language"] = app.Language.ToString(),
            ["internalUnits"] = "Revit API values are in feet (length), square feet, cubic feet and radians.",
        };

        var active = App.ActiveUIDocument?.Document;
        info["documents"] = Arr(app.Documents.Cast<Document>().Select(d => (JsonNode?)new JsonObject
        {
            ["title"] = d.Title,
            ["path"] = d.PathName,
            ["isActive"] = active is not null && d.Equals(active),
            ["isFamilyDocument"] = d.IsFamilyDocument,
            ["isWorkshared"] = d.IsWorkshared,
            ["isLinked"] = d.IsLinked,
            ["isModified"] = d.IsModified,
        }));

        if (active is not null)
        {
            var view = App.ActiveUIDocument!.ActiveView;
            info["activeDocument"] = active.Title;
            info["activeView"] = view is null ? null : new JsonObject
            {
                ["id"] = view.Id.IdValue(),
                ["name"] = view.Name,
                ["viewType"] = view.ViewType.ToString(),
            };
            var lengthUnit = active.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId();
            info["projectLengthUnit"] = new JsonObject
            {
                ["typeId"] = lengthUnit.TypeId,
                ["label"] = LabelUtils.GetLabelForUnit(lengthUnit),
            };
            info["selectionCount"] = App.ActiveUIDocument.Selection.GetElementIds().Count;
        }
        return info;
    }

    private static ElementId ResolveCategory(Document doc, string category)
    {
        string c = category.Trim();
        if (Enum.TryParse(c, true, out BuiltInCategory bic) || Enum.TryParse("OST_" + c, true, out bic))
            return new ElementId(bic);

        foreach (Category cat in doc.Settings.Categories)
        {
            if (string.Equals(cat.Name, c, StringComparison.OrdinalIgnoreCase)) return cat.Id;
            foreach (Category sub in cat.SubCategories)
                if (string.Equals(sub.Name, c, StringComparison.OrdinalIgnoreCase)) return sub.Id;
        }
        throw new BridgeException($"Unknown category '{category}'. Use a BuiltInCategory name like 'OST_Walls' or the category name shown in Revit.");
    }

    private static Type ResolveClass(string className)
    {
        var asm = typeof(Element).Assembly;
        var type = asm.GetType(className, false, true)
                   ?? asm.GetType("Autodesk.Revit.DB." + className, false, true)
                   ?? asm.GetType("Autodesk.Revit.DB.Architecture." + className, false, true)
                   ?? asm.GetType("Autodesk.Revit.DB.Structure." + className, false, true)
                   ?? asm.GetType("Autodesk.Revit.DB.Mechanical." + className, false, true)
                   ?? asm.GetType("Autodesk.Revit.DB.Plumbing." + className, false, true)
                   ?? asm.GetType("Autodesk.Revit.DB.Electrical." + className, false, true);
        if (type is null || !typeof(Element).IsAssignableFrom(type))
            throw new BridgeException($"'{className}' is not a Revit Element class.");
        return type;
    }

    private static JsonNode ListElements(JsonObject p)
    {
        var doc = Doc;
        int limit = Compat.Clamp(p.Int("limit", 200), 1, 5000);
        int offset = Math.Max(0, p.Int("offset", 0));

        var viewId = p.Long("viewId");
        var collector = viewId is long v ? new FilteredElementCollector(doc, Id(v)) : new FilteredElementCollector(doc);
        collector = p.Bool("typesOnly", false) ? collector.WhereElementIsElementType() : collector.WhereElementIsNotElementType();

        if (p.Str("category") is { Length: > 0 } category)
            collector = collector.OfCategoryId(ResolveCategory(doc, category));

        IEnumerable<Element> elements;
        if (p.Str("className") is { Length: > 0 } className)
        {
            var type = ResolveClass(className);
            try
            {
                elements = collector.OfClass(type);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Some subclasses (e.g. Room) cannot be used with OfClass; filter in memory instead.
                elements = collector.Where(type.IsInstanceOfType);
            }
        }
        else
        {
            elements = collector;
        }

        if (p.Str("nameContains") is { Length: > 0 } fragment)
            elements = elements.Where(e => Compat.ContainsIgnoreCase(RevitJson.SafeName(e), fragment));

        var all = elements.ToList();
        return new JsonObject
        {
            ["total"] = all.Count,
            ["offset"] = offset,
            ["returned"] = Math.Max(0, Math.Min(limit, all.Count - offset)),
            ["elements"] = Arr(all.Skip(offset).Take(limit).Select(e => (JsonNode?)RevitJson.Summary(e))),
        };
    }

    private static JsonNode GetElements(JsonObject p)
    {
        var doc = Doc;
        bool includeParameters = p.Bool("includeParameters", true);
        bool includeType = p.Bool("includeTypeParameters", false);

        return Arr(p.ReqLongs("elementIds").Select(id =>
        {
            var e = doc.GetElement(Id(id));
            if (e is null) return (JsonNode?)new JsonObject { ["id"] = id, ["error"] = "not found" };

            var o = RevitJson.Summary(e);
            var bb = e.get_BoundingBox(null);
            if (bb is not null) o["boundingBox"] = new JsonObject { ["min"] = RevitJson.Xyz(bb.Min), ["max"] = RevitJson.Xyz(bb.Max) };
            if (includeParameters) o["parameters"] = RevitJson.Parameters(e);
            if (includeType && doc.GetElement(e.GetTypeId()) is ElementType type)
                o["typeParameters"] = RevitJson.Parameters(type);
            return o;
        }));
    }

    private static JsonNode GetSelection()
    {
        var doc = Doc;
        var ids = UiDoc.Selection.GetElementIds();
        return new JsonObject
        {
            ["count"] = ids.Count,
            ["elements"] = Arr(ids.Take(1000).Select(id => doc.GetElement(id)).Where(e => e is not null)
                .Select(e => (JsonNode?)RevitJson.Summary(e!))),
        };
    }

    private static JsonNode SetSelection(JsonObject p)
    {
        var ids = p.ReqLongs("elementIds").Select(Id).ToList();
        UiDoc.Selection.SetElementIds(ids);
        return new JsonObject { ["selected"] = ids.Count };
    }

    private static JsonNode ListViews(JsonObject p)
    {
        var doc = Doc;
        ViewType? filter = null;
        if (p.Str("viewType") is { Length: > 0 } vt)
        {
            if (!Enum.TryParse(vt, true, out ViewType parsed))
                throw new BridgeException($"Unknown viewType '{vt}'. Valid: {string.Join(", ", Enum.GetNames(typeof(ViewType)))}");
            filter = parsed;
        }

        var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && (filter is null || v.ViewType == filter))
            .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase);

        return Arr(views.Select(v =>
        {
            var o = new JsonObject
            {
                ["id"] = v.Id.IdValue(),
                ["name"] = v.Name,
                ["viewType"] = v.ViewType.ToString(),
            };
            if (v.GenLevel is { } level) o["levelName"] = level.Name;
            if (v is ViewSheet sheet) o["sheetNumber"] = sheet.SheetNumber;
            return (JsonNode?)o;
        }));
    }

    private static JsonNode ListLevels()
    {
        var doc = Doc;
        var units = doc.GetUnits();
        return Arr(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => (JsonNode?)new JsonObject
            {
                ["id"] = l.Id.IdValue(),
                ["name"] = l.Name,
                ["elevationFeet"] = RevitJson.R(l.Elevation),
                ["elevationDisplay"] = UnitFormatUtils.Format(units, SpecTypeId.Length, l.Elevation, false),
            }));
    }

    private static JsonNode GetScheduleData(JsonObject p)
    {
        var doc = Doc;
        ViewSchedule? schedule = null;
        if (p.Long("scheduleId") is long id) schedule = doc.GetElement(Id(id)) as ViewSchedule;
        else if (p.Str("scheduleName") is { Length: > 0 } name)
            schedule = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        else throw new BridgeException("Pass scheduleId or scheduleName.");

        if (schedule is null) throw new BridgeException("Schedule not found.");
        int maxRows = Math.Max(1, p.Int("maxRows", 1000));

        JsonArray ReadSection(SectionType type, int max)
        {
            var section = schedule.GetTableData().GetSectionData(type);
            var rows = new JsonArray();
            if (section is null) return rows;
            for (int r = section.FirstRowNumber; r <= section.LastRowNumber && rows.Count < max; r++)
            {
                var row = new JsonArray();
                for (int c = section.FirstColumnNumber; c <= section.LastColumnNumber; c++)
                    row.Add(schedule.GetCellText(type, r, c));
                rows.Add(row);
            }
            return rows;
        }

        var body = schedule.GetTableData().GetSectionData(SectionType.Body);
        return new JsonObject
        {
            ["id"] = schedule.Id.IdValue(),
            ["name"] = schedule.Name,
            ["header"] = ReadSection(SectionType.Header, 100),
            ["totalBodyRows"] = body?.NumberOfRows ?? 0,
            ["body"] = ReadSection(SectionType.Body, maxRows),
        };
    }

    // ---------------------------------------------------------------- write

    private static Parameter FindParameter(Element e, string name)
    {
        var parameter = e.LookupParameter(name);
        if (parameter is null && Enum.TryParse(name, true, out BuiltInParameter bip)) parameter = e.get_Parameter(bip);
        if (parameter is not null) return parameter;

        bool onType = e.Document.GetElement(e.GetTypeId()) is ElementType type &&
                      (type.LookupParameter(name) is not null ||
                       (Enum.TryParse(name, true, out BuiltInParameter tb) && type.get_Parameter(tb) is not null));
        throw new BridgeException(onType
            ? $"Parameter '{name}' is a type parameter of element {e.Id.IdValue()}; set it on type id {e.GetTypeId().IdValue()} instead."
            : $"Element {e.Id.IdValue()} has no parameter '{name}'.");
    }

    private static void SetValue(Parameter parameter, string value, bool internalMode)
    {
        string name = parameter.Definition.Name;
        bool ok;
        switch (parameter.StorageType)
        {
            case StorageType.String:
                ok = parameter.Set(value);
                break;
            case StorageType.ElementId:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                    throw new BridgeException($"'{name}' expects an element id.");
                ok = parameter.Set(RevitCompat.ToElementId(id));
                break;
            case StorageType.Integer:
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)) ok = parameter.Set(1);
                else if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase)) ok = parameter.Set(0);
                else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) ok = parameter.Set(i);
                else ok = !internalMode && parameter.SetValueString(value);
                break;
            case StorageType.Double:
                if (internalMode)
                {
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        throw new BridgeException($"'{name}' expects a number in internal units.");
                    ok = parameter.Set(d);
                }
                else
                {
                    ok = parameter.SetValueString(value);
                }
                break;
            default:
                throw new BridgeException($"'{name}' has no settable value.");
        }
        if (!ok) throw new BridgeException($"Revit rejected value '{value}' for '{name}' ({parameter.StorageType}).");
    }

    private static JsonNode SetParameters(JsonObject p)
    {
        var doc = Doc;
        var updates = p.ReqArray("updates");
        return RevitTransactions.Run(doc, "MCP: Set parameters", () =>
        {
            var results = new JsonArray();
            foreach (var node in updates)
            {
                var u = Params.Obj(node, "updates[]");
                long id = u.ReqLong("elementId");
                string name = u.ReqStr("parameter");
                string value = u.Str("value") ?? throw new BridgeException("Each update needs 'value'.");
                bool internalMode = string.Equals(u.Str("mode"), "internal", StringComparison.OrdinalIgnoreCase);

                var e = doc.GetElement(Id(id)) ?? throw new BridgeException($"Element {id} not found.");
                var parameter = FindParameter(e, name);
                if (parameter.IsReadOnly) throw new BridgeException($"Parameter '{name}' on element {id} is read-only.");
                SetValue(parameter, value, internalMode);
                results.Add(new JsonObject
                {
                    ["elementId"] = id,
                    ["parameter"] = parameter.Definition.Name,
                    ["newValue"] = parameter.AsValueString() ?? parameter.AsString(),
                });
            }
            return results;
        });
    }

    private static ForgeTypeId LengthUnit(string units) => units.Trim().ToLowerInvariant() switch
    {
        "mm" or "millimeter" or "millimeters" => UnitTypeId.Millimeters,
        "cm" or "centimeter" or "centimeters" => UnitTypeId.Centimeters,
        "m" or "meter" or "meters" => UnitTypeId.Meters,
        "ft" or "feet" or "foot" => UnitTypeId.Feet,
        "in" or "inch" or "inches" => UnitTypeId.Inches,
        _ => throw new BridgeException($"Unsupported units '{units}'. Use mm, cm, m, ft or in."),
    };

    private static JsonNode CreateWalls(JsonObject p)
    {
        var doc = Doc;
        if (doc.IsFamilyDocument) throw new BridgeException("Walls can only be created in a project document.");

        var unit = LengthUnit(p.ReqStr("units"));
        double ToFt(double v) => UnitUtils.ConvertToInternalUnits(v, unit);

        var level = doc.GetElement(Id(p.ReqLong("levelId"))) as Level ?? throw new BridgeException("levelId is not a Level.");
        double height = ToFt(p.ReqDouble("height"));
        if (height <= 0) throw new BridgeException("height must be positive.");

        var wallTypeId = p.Long("wallTypeId") is long wt ? Id(wt) : doc.GetDefaultElementTypeId(ElementTypeGroup.WallType);
        if (doc.GetElement(wallTypeId) is not WallType) throw new BridgeException("wallTypeId is not a WallType.");
        bool structural = p.Bool("structural", false);
        double tolerance = App.Application.ShortCurveTolerance;

        var walls = p.ReqArray("walls");
        return RevitTransactions.Run(doc, "MCP: Create walls", () =>
        {
            var created = new JsonArray();
            int index = 0;
            foreach (var node in walls)
            {
                var w = Params.Obj(node, $"walls[{index}]");
                var s = Params.Point(w["start"], $"walls[{index}].start");
                var e = Params.Point(w["end"], $"walls[{index}].end");
                var start = new XYZ(ToFt(s[0]), ToFt(s[1]), level.Elevation);
                var end = new XYZ(ToFt(e[0]), ToFt(e[1]), level.Elevation);
                if (start.DistanceTo(end) <= tolerance) throw new BridgeException($"walls[{index}] is too short.");

                var wall = Wall.Create(doc, Line.CreateBound(start, end), wallTypeId, level.Id, height, 0, false, structural);
                created.Add(wall.Id.IdValue());
                index++;
            }
            return new JsonObject { ["createdIds"] = created };
        });
    }

    private static JsonNode DeleteElements(JsonObject p)
    {
        var doc = Doc;
        var ids = p.ReqLongs("elementIds").Select(Id).ToList();
        foreach (var id in ids)
            if (doc.GetElement(id) is null) throw new BridgeException($"Element {id.IdValue()} not found.");

        return RevitTransactions.Run(doc, "MCP: Delete elements", () =>
        {
            var deleted = doc.Delete(ids);
            return new JsonObject
            {
                ["requested"] = ids.Count,
                ["deletedCount"] = deleted.Count,
                ["deletedIds"] = Arr(deleted.Select(d => (JsonNode?)d.IdValue())),
            };
        });
    }

    // ---------------------------------------------------------------- code execution

    private static JsonNode ExecuteScript(JsonObject p)
    {
        byte[] assembly = Convert.FromBase64String(p.ReqStr("assembly"));
        string? transactionName = p.Str("transactionName");
        var output = new StringBuilder();
        var app = App;

        JsonObject response;
        if (string.IsNullOrWhiteSpace(transactionName))
        {
            object? result = ScriptRunner.Run(assembly, new object?[] { app, output });
            response = new JsonObject { ["result"] = RevitJson.ToJson(result) };
        }
        else
        {
            response = RevitTransactions.Run(Doc, transactionName!, () =>
                RevitJson.ToJson(ScriptRunner.Run(assembly, new object?[] { app, output })));
        }

        if (output.Length > 0) response["output"] = output.ToString();
        return response;
    }
}
