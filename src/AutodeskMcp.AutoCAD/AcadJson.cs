using System;
using System.Linq;
using System.Text.Json.Nodes;
using AutodeskMcp.Bridge;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutodeskMcp.AutoCAD;

/// <summary>JSON shapes for AutoCAD API objects. Coordinates are WCS drawing units; angles in degrees.</summary>
internal static class AcadJson
{
    public static JsonNode? Convert(object value, Func<object?, JsonNode?> recurse) => value switch
    {
        ObjectId id => id.IsNull ? null : JsonValue.Create(id.Handle.ToString()),
        Handle h => JsonValue.Create(h.ToString()),
        Point3d p => P(p),
        Point2d p => new JsonArray(R(p.X), R(p.Y)),
        Vector3d v => new JsonArray(R(v.X), R(v.Y), R(v.Z)),
        Vector2d v => new JsonArray(R(v.X), R(v.Y)),
        Scale3d s => new JsonArray(R(s.X), R(s.Y), R(s.Z)),
        Extents3d e => new JsonObject { ["min"] = P(e.MinPoint), ["max"] = P(e.MaxPoint) },
        Matrix3d m => new JsonArray(m.ToArray().Select(d => (JsonNode?)R(d)).ToArray()),
        Color c => new JsonObject { ["colorIndex"] = c.ColorIndex, ["description"] = c.ToString() },
        Entity e => Summary(e, null),
        DBObject o => new JsonObject { ["handle"] = o.Handle.ToString(), ["className"] = o.GetType().Name },
        _ => null,
    };

    public static JsonNode? ToJson(object? value) => SafeJson.ToNode(value, Convert);

    public static double R(double v) => Math.Round(v, 8);
    public static double Deg(double radians) => Math.Round(radians * 180.0 / Math.PI, 8);
    public static JsonArray P(Point3d p) => new(R(p.X), R(p.Y), R(p.Z));

    /// <param name="tr">Optional open transaction, used to resolve block names and attributes.</param>
    public static JsonObject Summary(Entity e, Transaction? tr, bool details = false)
    {
        var o = new JsonObject
        {
            ["handle"] = e.Handle.ToString(),
            ["type"] = e.GetRXClass().DxfName,
            ["className"] = e.GetType().Name,
            ["layer"] = e.Layer,
            ["colorIndex"] = e.ColorIndex,
            ["linetype"] = e.Linetype,
        };

        switch (e)
        {
            case Line l:
                o["start"] = P(l.StartPoint);
                o["end"] = P(l.EndPoint);
                o["length"] = R(l.Length);
                break;
            case Circle c:
                o["center"] = P(c.Center);
                o["radius"] = R(c.Radius);
                break;
            case Arc a:
                o["center"] = P(a.Center);
                o["radius"] = R(a.Radius);
                o["startAngle"] = Deg(a.StartAngle);
                o["endAngle"] = Deg(a.EndAngle);
                o["length"] = R(a.Length);
                break;
            case Polyline pl:
                int max = details ? 5000 : 200;
                var vertices = new JsonArray();
                for (int i = 0; i < pl.NumberOfVertices && i < max; i++)
                {
                    var pt = pl.GetPoint2dAt(i);
                    double bulge = pl.GetBulgeAt(i);
                    vertices.Add(bulge == 0 ? new JsonArray(R(pt.X), R(pt.Y)) : new JsonArray(R(pt.X), R(pt.Y), R(bulge)));
                }
                o["vertexCount"] = pl.NumberOfVertices;
                o["vertices"] = vertices;
                o["verticesNote"] = "[x, y] or [x, y, bulge]";
                o["closed"] = pl.Closed;
                o["elevation"] = R(pl.Elevation);
                o["length"] = R(pl.Length);
                if (pl.Closed) o["area"] = R(pl.Area);
                break;
            case DBText t:
                o["text"] = t.TextString;
                o["position"] = P(t.Position);
                o["height"] = R(t.Height);
                o["rotation"] = Deg(t.Rotation);
                break;
            case MText mt:
                o["text"] = mt.Text;
                if (details) o["contents"] = mt.Contents;
                o["location"] = P(mt.Location);
                o["height"] = R(mt.TextHeight);
                o["width"] = R(mt.Width);
                break;
            case DBPoint pt:
                o["position"] = P(pt.Position);
                break;
            case BlockReference br:
                o["blockName"] = EffectiveName(br, tr);
                o["position"] = P(br.Position);
                o["rotation"] = Deg(br.Rotation);
                o["scale"] = new JsonArray(R(br.ScaleFactors.X), R(br.ScaleFactors.Y), R(br.ScaleFactors.Z));
                if (tr is not null && br.AttributeCollection.Count > 0)
                {
                    var attrs = new JsonObject();
                    foreach (ObjectId attId in br.AttributeCollection)
                        if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference ar) attrs[ar.Tag] = ar.TextString;
                    o["attributes"] = attrs;
                }
                if (details && br.IsDynamicBlock)
                {
                    var props = new JsonArray();
                    foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                        props.Add(new JsonObject
                        {
                            ["name"] = prop.PropertyName,
                            ["value"] = SafeJson.ToNode(prop.Value, Convert),
                            ["unitsType"] = prop.UnitsType.ToString(),
                            ["readOnly"] = prop.ReadOnly,
                        });
                    o["dynamicProperties"] = props;
                }
                break;
            case Dimension d:
                o["measurement"] = R(d.Measurement);
                o["dimensionText"] = d.DimensionText;
                break;
            case Hatch h:
                o["patternName"] = h.PatternName;
                try { o["area"] = R(h.Area); } catch { /* invalid boundary */ }
                break;
            case Curve curve:
                try { o["length"] = R(curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam)); }
                catch { /* not all curves support it */ }
                break;
        }

        if (details)
        {
            try { o["extents"] = new JsonObject { ["min"] = P(e.GeometricExtents.MinPoint), ["max"] = P(e.GeometricExtents.MaxPoint) }; }
            catch { /* empty / invalid geometry */ }
            o["lineweight"] = e.LineWeight.ToString();
            o["visible"] = e.Visible;
        }
        return o;
    }

    public static string EffectiveName(BlockReference br, Transaction? tr)
    {
        if (tr is null) return br.Name;
        var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
        return tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr ? btr.Name : br.Name;
    }
}
