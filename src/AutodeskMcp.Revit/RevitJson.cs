using System;
using System.Linq;
using System.Text.Json.Nodes;
using AutodeskMcp.Bridge;
using Autodesk.Revit.DB;

namespace AutodeskMcp.Revit;

/// <summary>JSON shapes for Revit API objects. Coordinates are Revit internal units (feet).</summary>
internal static class RevitJson
{
    public static JsonNode? Convert(object value, Func<object?, JsonNode?> recurse) => value switch
    {
        ElementId id => JsonValue.Create(id.Value),
        XYZ p => Xyz(p),
        UV uv => new JsonArray(R(uv.U), R(uv.V)),
        Element e => Summary(e),
        Parameter p => ParameterJson(p),
        Curve c => CurveJson(c),
        BoundingBoxXYZ bb => new JsonObject { ["min"] = Xyz(bb.Min), ["max"] = Xyz(bb.Max) },
        Category c => new JsonObject { ["id"] = c.Id.Value, ["name"] = c.Name },
        ForgeTypeId f => JsonValue.Create(f.TypeId),
        Transform t => new JsonObject { ["origin"] = Xyz(t.Origin), ["basisX"] = Xyz(t.BasisX), ["basisY"] = Xyz(t.BasisY), ["basisZ"] = Xyz(t.BasisZ) },
        _ => null,
    };

    public static JsonNode? ToJson(object? value) => SafeJson.ToNode(value, Convert);

    public static double R(double v) => Math.Round(v, 6);

    public static JsonArray Xyz(XYZ p) => new(R(p.X), R(p.Y), R(p.Z));

    public static string SafeName(Element e)
    {
        try { return e.Name ?? ""; } catch { return ""; }
    }

    public static JsonObject Summary(Element e)
    {
        var doc = e.Document;
        var o = new JsonObject
        {
            ["id"] = e.Id.Value,
            ["name"] = SafeName(e),
            ["className"] = e.GetType().Name,
        };

        if (e.Category is { } cat)
        {
            o["category"] = cat.Name;
            o["builtInCategory"] = cat.BuiltInCategory.ToString();
        }

        var typeId = e.GetTypeId();
        if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType type)
        {
            o["typeId"] = typeId.Value;
            o["typeName"] = SafeName(type);
            o["familyName"] = type.FamilyName;
        }

        if (e.LevelId != ElementId.InvalidElementId && doc.GetElement(e.LevelId) is Level level)
        {
            o["levelId"] = level.Id.Value;
            o["levelName"] = level.Name;
        }

        switch (e.Location)
        {
            case LocationPoint lp:
                o["location"] = new JsonObject { ["point"] = Xyz(lp.Point), ["rotation"] = R(SafeRotation(lp)) };
                break;
            case LocationCurve lc:
                o["location"] = CurveJson(lc.Curve);
                break;
        }
        return o;
    }

    private static double SafeRotation(LocationPoint lp)
    {
        try { return lp.Rotation; } catch { return 0; }
    }

    public static JsonObject CurveJson(Curve c)
    {
        var o = new JsonObject { ["curveType"] = c.GetType().Name };
        if (c.IsBound)
        {
            o["start"] = Xyz(c.GetEndPoint(0));
            o["end"] = Xyz(c.GetEndPoint(1));
            o["length"] = R(c.Length);
        }
        return o;
    }

    public static JsonObject ParameterJson(Parameter p)
    {
        var o = new JsonObject
        {
            ["name"] = p.Definition?.Name,
            ["value"] = p.HasValue ? (p.AsValueString() ?? p.AsString()) : null,
            ["storageType"] = p.StorageType.ToString(),
            ["readOnly"] = p.IsReadOnly,
            ["shared"] = p.IsShared,
        };

        if (p.HasValue)
        {
            o["raw"] = p.StorageType switch
            {
                StorageType.Double => JsonValue.Create(R(p.AsDouble())),
                StorageType.Integer => JsonValue.Create(p.AsInteger()),
                StorageType.String => JsonValue.Create(p.AsString()),
                StorageType.ElementId => JsonValue.Create(p.AsElementId().Value),
                _ => null,
            };
        }

        if (p.Definition is InternalDefinition { BuiltInParameter: var bip } && bip != BuiltInParameter.INVALID)
            o["builtInParameter"] = bip.ToString();

        try
        {
            var dataType = p.Definition?.GetDataType();
            if (dataType is not null && !dataType.Empty()) o["dataType"] = dataType.TypeId;
        }
        catch { /* some definitions throw */ }

        return o;
    }

    public static JsonArray Parameters(Element e) => new(
        e.Parameters.Cast<Parameter>()
            .Where(p => p.Definition is not null)
            .OrderBy(p => p.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (JsonNode?)ParameterJson(p))
            .ToArray());
}
