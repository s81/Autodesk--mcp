using Autodesk.Revit.DB;

namespace AutodeskMcp.Revit;

/// <summary>
/// Differences between Revit API versions, in one place.
/// Revit 2024 changed ElementId from 32-bit (IntegerValue, new ElementId(int)) to 64-bit (Value, new ElementId(long)).
/// The bridge always exchanges ids as 64-bit numbers.
/// </summary>
internal static class RevitCompat
{
    public static long IdValue(this ElementId id) =>
#if REVIT2022 || REVIT2023
        id.IntegerValue;
#else
        id.Value;
#endif

    public static ElementId ToElementId(long value)
    {
#if REVIT2022 || REVIT2023
        if (value < int.MinValue || value > int.MaxValue)
            throw new AutodeskMcp.Bridge.BridgeException($"Element id {value} is out of range for this Revit version.");
        return new ElementId((int)value);
#else
        return new ElementId(value);
#endif
    }

    /// <summary>The BuiltInCategory name (e.g. "OST_Walls"), or "INVALID" for user-defined categories.</summary>
    public static string BuiltInCategoryName(Category category) =>
#if REVIT2022
        // Category.BuiltInCategory was added in Revit 2023; built-in categories have negative ids equal to the enum value.
        System.Enum.IsDefined(typeof(BuiltInCategory), category.Id.IntegerValue)
            ? ((BuiltInCategory)category.Id.IntegerValue).ToString()
            : BuiltInCategory.INVALID.ToString();
#else
        category.BuiltInCategory.ToString();
#endif
}
