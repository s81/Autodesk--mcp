using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
#if NETFRAMEWORK
using AutodeskMcp.Bridge.Json;
#else
using System.Text.Json.Nodes;
#endif
using AutodeskMcp.Bridge;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutodeskMcp.AutoCAD;

/// <summary>Bridge methods implemented against the AutoCAD .NET API. Every handler runs on AutoCAD's main thread.</summary>
internal static class AcadCommands
{
    public static IEnumerable<BridgeCommand> Create() => new[]
    {
        new BridgeCommand("session_info", _ => SessionInfo()),
        new BridgeCommand("list_layers", _ => ListLayers()),
        new BridgeCommand("list_entities", ListEntities),
        new BridgeCommand("get_entities", GetEntities),
        new BridgeCommand("list_blocks", ListBlocks),
        new BridgeCommand("create_layer", CreateLayer) { Modifies = true },
        new BridgeCommand("create_entities", CreateEntities) { Modifies = true },
        new BridgeCommand("modify_entities", ModifyEntities) { Modifies = true },
        new BridgeCommand("insert_block", InsertBlock) { Modifies = true },
        new BridgeCommand("send_command", SendCommand) { Modifies = true },
        new BridgeCommand("execute_script", ExecuteScript) { Modifies = true, ExecutesCode = true },
    };

    private static Document Doc =>
        AcApp.DocumentManager.MdiActiveDocument ?? throw new BridgeException("No active drawing. Open or create a drawing first.");

    private static JsonArray Arr(IEnumerable<JsonNode?> items) => new(items.ToArray());

    /// <summary>Read-only access that is safe even while a command is running (no document lock).</summary>
    private static T Read<T>(Func<Database, Transaction, T> body)
    {
        var db = Doc.Database;
        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        var result = body(db, tr);
        tr.Commit();
        return result;
    }

    /// <summary>Locked, single-undo-step write. Refuses to run in the middle of a user command.</summary>
    private static T Write<T>(Func<Database, Transaction, T> body)
    {
        var doc = Doc;
        RequireQuiescent(doc);
        using (doc.LockDocument())
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var result = body(doc.Database, tr);
            tr.Commit(); // exceptions skip this, and Dispose aborts
            return result;
        }
    }

    private static void RequireQuiescent(Document doc)
    {
        if (!doc.Editor.IsQuiescent)
            throw new BridgeException(
                $"AutoCAD is busy with a command ({doc.CommandInProgress}). Finish or cancel it (Esc), then retry.");
    }

    private static ObjectId ParseHandle(Database db, string handle)
    {
        try
        {
            var h = new Handle(Convert.ToInt64(handle.Trim(), 16));
            var id = db.GetObjectId(false, h, 0);
            if (id.IsNull || id.IsErased) throw new BridgeException($"Handle {handle} is erased.");
            return id;
        }
        catch (BridgeException) { throw; }
        catch (Exception)
        {
            throw new BridgeException($"No object with handle '{handle}'.");
        }
    }

    private static ObjectId SpaceId(Database db, Transaction tr, string? space)
    {
        if (space is null || string.IsNullOrWhiteSpace(space) || space.Equals("model", StringComparison.OrdinalIgnoreCase) ||
            space.Equals("modelspace", StringComparison.OrdinalIgnoreCase))
            return SymbolUtilityServices.GetBlockModelSpaceId(db);

        var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layouts)
        {
            if (string.Equals(entry.Key, space, StringComparison.OrdinalIgnoreCase))
                return ((Layout)tr.GetObject(entry.Value, OpenMode.ForRead)).BlockTableRecordId;
        }
        throw new BridgeException($"Unknown space '{space}'. Use 'model' or one of: {string.Join(", ", LayoutNames(db, tr))}");
    }

    private static IEnumerable<string> LayoutNames(Database db, Transaction tr)
    {
        var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        return layouts.Cast<DBDictionaryEntry>()
            .Select(e => (Layout)tr.GetObject(e.Value, OpenMode.ForRead))
            .OrderBy(l => l.TabOrder)
            .Select(l => l.LayoutName)
            .ToList();
    }

    private static Point3d Pt(JsonNode? node, string what)
    {
        var p = Params.Point(node, what);
        return new Point3d(p[0], p[1], p[2]);
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180.0;

    // ---------------------------------------------------------------- read

    private static JsonNode SessionInfo()
    {
        var dm = AcApp.DocumentManager;
        var active = dm.MdiActiveDocument;
        var info = new JsonObject
        {
            ["product"] = AcApp.GetSystemVariable("PRODUCT")?.ToString(),
            ["version"] = AcApp.Version.ToString(),
            ["productYear"] = BridgeExtension.ProductYear(AcApp.Version),
            ["acadver"] = AcApp.GetSystemVariable("ACADVER")?.ToString(),
            ["documents"] = Arr(dm.Cast<Document>().Select(d => (JsonNode?)new JsonObject
            {
                ["name"] = d.Name,
                ["isActive"] = d == active,
                ["isReadOnly"] = d.IsReadOnly,
            })),
        };

        if (active is null) return info;

        var db = active.Database;
        info["activeDocument"] = active.Name;
        info["isQuiescent"] = active.Editor.IsQuiescent;
        info["commandInProgress"] = active.CommandInProgress;
        info["insunits"] = db.Insunits.ToString();
        info["lunits"] = db.Lunits;
        info["luprec"] = db.Luprec;
        info["textsize"] = db.Textsize;
        info["modified"] = Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0;
        info["extents"] = new JsonObject { ["min"] = AcadJson.P(db.Extmin), ["max"] = AcadJson.P(db.Extmax) };

        Read((d, tr) =>
        {
            info["currentLayer"] = ((LayerTableRecord)tr.GetObject(d.Clayer, OpenMode.ForRead)).Name;
            info["layouts"] = Arr(LayoutNames(d, tr).Select(n => (JsonNode?)n));
            return 0;
        });
        return info;
    }

    private static JsonNode ListLayers() => Read((db, tr) =>
    {
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var result = new JsonArray();
        foreach (ObjectId id in table)
        {
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            var linetype = tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead) as LinetypeTableRecord;
            result.Add(new JsonObject
            {
                ["name"] = layer.Name,
                ["colorIndex"] = layer.Color.ColorIndex,
                ["color"] = layer.Color.ToString(),
                ["linetype"] = linetype?.Name,
                ["lineweight"] = layer.LineWeight.ToString(),
                ["isOff"] = layer.IsOff,
                ["isFrozen"] = layer.IsFrozen,
                ["isLocked"] = layer.IsLocked,
                ["isPlottable"] = layer.IsPlottable,
                ["isCurrent"] = id == db.Clayer,
                ["isXref"] = layer.IsDependent,
                ["description"] = layer.Description,
            });
        }
        return (JsonNode)result;
    });

    private static JsonNode ListEntities(JsonObject p)
    {
        int limit = Compat.Clamp(p.Int("limit", 500), 1, 10000);
        int offset = Math.Max(0, p.Int("offset", 0));
        var types = new HashSet<string>(p.Strings("types"), StringComparer.OrdinalIgnoreCase);
        string? layer = p.Str("layer");

        return Read((db, tr) =>
        {
            var space = (BlockTableRecord)tr.GetObject(SpaceId(db, tr, p.Str("space")), OpenMode.ForRead);
            var items = new JsonArray();
            int matched = 0;
            foreach (ObjectId id in space)
            {
                if (types.Count > 0 && !types.Contains(id.ObjectClass.DxfName)) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                if (layer is { Length: > 0 } && !string.Equals(e.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;
                matched++;
                if (matched <= offset || items.Count >= limit) continue;
                items.Add(AcadJson.Summary(e, tr));
            }
            return (JsonNode)new JsonObject
            {
                ["space"] = space.Name,
                ["total"] = matched,
                ["offset"] = offset,
                ["returned"] = items.Count,
                ["entities"] = items,
            };
        });
    }

    private static JsonNode GetEntities(JsonObject p)
    {
        var handles = p.Strings("handles");
        if (handles.Count == 0) throw new BridgeException("Pass at least one handle.");
        return Read((db, tr) => (JsonNode)Arr(handles.Select(h =>
        {
            try
            {
                var obj = tr.GetObject(ParseHandle(db, h), OpenMode.ForRead);
                return obj is Entity e
                    ? (JsonNode?)AcadJson.Summary(e, tr, details: true)
                    : new JsonObject { ["handle"] = h, ["className"] = obj.GetType().Name, ["note"] = "not an entity" };
            }
            catch (BridgeException ex)
            {
                return new JsonObject { ["handle"] = h, ["error"] = ex.Message };
            }
        })));
    }

    private static JsonNode ListBlocks(JsonObject p)
    {
        bool includeAnonymous = p.Bool("includeAnonymous", false);
        return Read((db, tr) =>
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var result = new JsonArray();
            foreach (ObjectId id in table)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (btr.IsLayout || (btr.IsAnonymous && !includeAnonymous)) continue;

                var tags = new JsonArray();
                if (btr.HasAttributeDefinitions)
                {
                    foreach (ObjectId childId in btr)
                        if (childId.ObjectClass.DxfName == "ATTDEF" && tr.GetObject(childId, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant)
                            tags.Add(ad.Tag);
                }

                result.Add(new JsonObject
                {
                    ["name"] = btr.Name,
                    ["isDynamic"] = btr.IsDynamicBlock,
                    ["isXref"] = btr.IsFromExternalReference,
                    ["isAnonymous"] = btr.IsAnonymous,
                    ["origin"] = AcadJson.P(btr.Origin),
                    ["attributeTags"] = tags,
                    ["referenceCount"] = btr.GetBlockReferenceIds(true, false).Count,
                    ["description"] = btr.Comments,
                });
            }
            return (JsonNode)result;
        });
    }

    // ---------------------------------------------------------------- write

    private static ObjectId RequireLayer(Database db, Transaction tr, string name)
    {
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!table.Has(name)) throw new BridgeException($"Layer '{name}' does not exist. Create it with autocad_create_layer.");
        return table[name];
    }

    private static Color Aci(int index)
    {
        if (index is < 0 or > 256) throw new BridgeException("colorIndex must be 0-256.");
        return index switch
        {
            256 => Color.FromColorIndex(ColorMethod.ByLayer, 256),
            0 => Color.FromColorIndex(ColorMethod.ByBlock, 0),
            _ => Color.FromColorIndex(ColorMethod.ByAci, (short)index),
        };
    }

    private static JsonNode CreateLayer(JsonObject p)
    {
        string name = p.ReqStr("name").Trim();
        try { SymbolUtilityServices.ValidateSymbolName(name, false); }
        catch (Exception) { throw new BridgeException($"'{name}' is not a valid layer name."); }

        return Write((db, tr) =>
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            bool created = !table.Has(name);
            LayerTableRecord layer;
            if (created)
            {
                table.UpgradeOpen();
                layer = new LayerTableRecord { Name = name };
                table.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
            }
            else
            {
                layer = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForWrite);
            }

            if (p.Long("colorIndex") is long ci)
            {
                if (ci is < 1 or > 255) throw new BridgeException("Layer colorIndex must be 1-255.");
                layer.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)ci);
            }
            if (p.Str("linetype") is { Length: > 0 } lt)
            {
                var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (!linetypes.Has(lt)) throw new BridgeException($"Linetype '{lt}' is not loaded in this drawing.");
                layer.LinetypeObjectId = linetypes[lt];
            }
            if (p["isOff"] is not null) layer.IsOff = p.Bool("isOff", false);
            if (p["isLocked"] is not null) layer.IsLocked = p.Bool("isLocked", false);
            if (p["isFrozen"] is not null)
            {
                bool freeze = p.Bool("isFrozen", false);
                if (freeze && layer.ObjectId == db.Clayer) throw new BridgeException("The current layer cannot be frozen.");
                layer.IsFrozen = freeze;
            }
            if (p.Bool("makeCurrent", false))
            {
                if (layer.IsFrozen) throw new BridgeException("A frozen layer cannot be made current.");
                db.Clayer = layer.ObjectId;
            }
            return (JsonNode)new JsonObject { ["name"] = name, ["created"] = created };
        });
    }

    private static Entity BuildEntity(JsonObject s, int index, Database db)
    {
        string type = s.ReqStr("type").Trim().ToLowerInvariant();
        string where = $"entities[{index}]";
        var points = s.Array("points");
        Point3d PointAt(int i) => points is not null && points.Count > i
            ? Pt(points[i], $"{where}.points[{i}]")
            : throw new BridgeException($"{where} ({type}) needs at least {i + 1} point(s).");
        double Radius() => s.Double("radius") is double r && r > 0 ? r : throw new BridgeException($"{where} needs a positive radius.");

        switch (type)
        {
            case "line":
                return new Line(PointAt(0), PointAt(1));
            case "circle":
                return new Circle(Pt(s["center"], where + ".center"), Vector3d.ZAxis, Radius());
            case "arc":
                return new Arc(Pt(s["center"], where + ".center"), Radius(),
                    Rad(s.Double("startAngle") ?? throw new BridgeException($"{where} needs startAngle.")),
                    Rad(s.Double("endAngle") ?? throw new BridgeException($"{where} needs endAngle.")));
            case "point":
                return new DBPoint(PointAt(0));
            case "polyline":
            {
                if (points is null || points.Count < 2) throw new BridgeException($"{where} polyline needs at least 2 points.");
                var pl = new Polyline();
                double elevation = 0;
                for (int i = 0; i < points.Count; i++)
                {
                    var pt = Pt(points[i], $"{where}.points[{i}]");
                    if (i == 0) elevation = pt.Z;
                    pl.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0, 0, 0);
                }
                pl.Elevation = elevation;
                pl.Closed = s.Bool("closed", false);
                return pl;
            }
            case "text":
                return new DBText
                {
                    Position = PointAt(0),
                    TextString = s.ReqStr("text"),
                    Height = s.Double("height") ?? db.Textsize,
                    Rotation = Rad(s.Double("rotation") ?? 0),
                };
            case "mtext":
                return new MText
                {
                    Location = PointAt(0),
                    Contents = s.ReqStr("text"),
                    TextHeight = s.Double("height") ?? db.Textsize,
                    Width = s.Double("width") ?? 0,
                    Rotation = Rad(s.Double("rotation") ?? 0),
                };
            default:
                throw new BridgeException($"{where}: unsupported type '{type}'. Use line, polyline, circle, arc, point, text or mtext.");
        }
    }

    private static JsonNode CreateEntities(JsonObject p)
    {
        var specs = p.ReqArray("entities");
        if (specs.Count == 0) throw new BridgeException("Pass at least one entity.");

        return Write((db, tr) =>
        {
            var space = (BlockTableRecord)tr.GetObject(SpaceId(db, tr, p.Str("space")), OpenMode.ForWrite);
            var handles = new JsonArray();
            for (int i = 0; i < specs.Count; i++)
            {
                var spec = Params.Obj(specs[i], $"entities[{i}]");
                var entity = BuildEntity(spec, i, db);
                entity.SetDatabaseDefaults(db);
                if (spec.Str("layer") is { Length: > 0 } layer) entity.LayerId = RequireLayer(db, tr, layer);
                if (spec.Long("colorIndex") is long ci) entity.Color = Aci((int)ci);
                space.AppendEntity(entity);
                tr.AddNewlyCreatedDBObject(entity, true);
                handles.Add(entity.Handle.ToString());
            }
            return (JsonNode)new JsonObject { ["createdHandles"] = handles };
        });
    }

    private static JsonNode ModifyEntities(JsonObject p)
    {
        var handles = p.Strings("handles");
        if (handles.Count == 0) throw new BridgeException("Pass at least one handle.");
        bool delete = p.Bool("delete", false);

        Matrix3d? transform = null;
        if (!delete)
        {
            var basePoint = p["basePoint"] is null ? Point3d.Origin : Pt(p["basePoint"], "basePoint");
            var m = Matrix3d.Identity;
            if (p.Double("scale") is double s)
            {
                if (s <= 0) throw new BridgeException("scale must be positive.");
                m = Matrix3d.Scaling(s, basePoint) * m;
            }
            if (p.Double("rotate") is double deg) m = Matrix3d.Rotation(Rad(deg), Vector3d.ZAxis, basePoint) * m;
            if (p["translate"] is not null)
            {
                var t = Params.Point(p["translate"], "translate");
                m = Matrix3d.Displacement(new Vector3d(t[0], t[1], t[2])) * m;
            }
            if (m != Matrix3d.Identity) transform = m;
        }

        return Write((db, tr) =>
        {
            ObjectId? layerId = p.Str("layer") is { Length: > 0 } layer ? RequireLayer(db, tr, layer) : null;
            Color? color = p.Long("colorIndex") is long ci ? Aci((int)ci) : null;
            if (!delete && layerId is null && color is null && transform is null)
                throw new BridgeException("Nothing to change: pass layer, colorIndex, translate, rotate, scale or delete.");

            int count = 0;
            foreach (string h in handles)
            {
                if (tr.GetObject(ParseHandle(db, h), OpenMode.ForWrite) is not Entity e)
                    throw new BridgeException($"Handle {h} is not an entity.");
                if (delete) { e.Erase(); count++; continue; }
                if (layerId is ObjectId lid) e.LayerId = lid;
                if (color is not null) e.Color = color;
                if (transform is Matrix3d m) e.TransformBy(m);
                count++;
            }
            return (JsonNode)new JsonObject { ["modified"] = count, ["deleted"] = delete };
        });
    }

    private static JsonNode InsertBlock(JsonObject p)
    {
        string blockName = p.ReqStr("blockName");
        var position = Pt(p["position"], "position");
        double scale = p.Double("scale") ?? 1;
        if (scale == 0) throw new BridgeException("scale cannot be 0.");
        double rotation = Rad(p.Double("rotation") ?? 0);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (p["attributes"] is JsonObject attrs)
            foreach (var kv in attrs) values[kv.Key] = kv.Value?.ToString() ?? "";

        return Write((db, tr) =>
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!table.Has(blockName)) throw new BridgeException($"Block '{blockName}' is not defined in this drawing.");
            var definition = (BlockTableRecord)tr.GetObject(table[blockName], OpenMode.ForRead);
            var space = (BlockTableRecord)tr.GetObject(SpaceId(db, tr, p.Str("space")), OpenMode.ForWrite);

            var reference = new BlockReference(position, definition.ObjectId)
            {
                ScaleFactors = new Scale3d(scale),
                Rotation = rotation,
            };
            reference.SetDatabaseDefaults(db);
            if (p.Str("layer") is { Length: > 0 } layer) reference.LayerId = RequireLayer(db, tr, layer);
            space.AppendEntity(reference);
            tr.AddNewlyCreatedDBObject(reference, true);

            var used = new List<string>();
            if (definition.HasAttributeDefinitions)
            {
                foreach (ObjectId id in definition)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition ad || ad.Constant) continue;
                    var ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, reference.BlockTransform);
                    if (values.TryGetValue(ad.Tag, out string? v)) { ar.TextString = v; used.Add(ad.Tag); }
                    reference.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                }
            }

            var unknown = values.Keys.Except(used, StringComparer.OrdinalIgnoreCase).ToList();
            var result = new JsonObject { ["handle"] = reference.Handle.ToString() };
            if (unknown.Count > 0) result["unknownAttributeTags"] = Arr(unknown.Select(u => (JsonNode?)u));
            return (JsonNode)result;
        });
    }

    private static JsonNode SendCommand(JsonObject p)
    {
        string command = p.Str("command") ?? throw new BridgeException("Missing 'command'.");
        if (command.Length == 0) throw new BridgeException("Empty command.");
        var doc = Doc;
        doc.SendStringToExecute(command, true, false, true);
        return new JsonObject
        {
            ["queued"] = true,
            ["note"] = "The command runs asynchronously after this call; inspect the drawing to see its effect.",
        };
    }

    // ---------------------------------------------------------------- code execution

    private static JsonNode ExecuteScript(JsonObject p)
    {
        byte[] assembly = Convert.FromBase64String(p.ReqStr("assembly"));
        bool commit = p.Bool("commit", true);
        var output = new StringBuilder();
        var doc = Doc;

        JsonObject response;
        if (commit)
        {
            RequireQuiescent(doc);
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                object? result = ScriptRunner.Run(assembly, new object?[] { doc, tr, output });
                response = new JsonObject { ["result"] = AcadJson.ToJson(result) };
                tr.Commit();
                response["committed"] = true;
            }
        }
        else
        {
            using var tr = doc.Database.TransactionManager.StartOpenCloseTransaction();
            object? result = ScriptRunner.Run(assembly, new object?[] { doc, tr, output });
            response = new JsonObject { ["result"] = AcadJson.ToJson(result), ["committed"] = false };
            tr.Abort();
        }

        if (output.Length > 0) response["output"] = output.ToString();
        return response;
    }
}
