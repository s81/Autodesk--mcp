using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutodeskMcp.Bridge;

/// <summary>Describes one running Revit/AutoCAD process that has the bridge add-in loaded.</summary>
public sealed class InstanceInfo
{
    public string InstanceId { get; set; } = "";
    /// <summary>"revit" or "autocad".</summary>
    public string Product { get; set; } = "";
    /// <summary>Product year, e.g. "2026".</summary>
    public string ProductVersion { get; set; } = "";
    public string ProductBuild { get; set; } = "";
    public string AddinVersion { get; set; } = "";
    public string RuntimeVersion { get; set; } = "";
    public int ProcessId { get; set; }
    public long ProcessStartTicksUtc { get; set; }
    public string PipeName { get; set; } = "";
    /// <summary>Shared secret required on every request. The file lives in the user's profile.</summary>
    public string Token { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
}

/// <summary>
/// File-based discovery. Each add-in writes %LOCALAPPDATA%\AutodeskMcp\instances\{id}.json on startup
/// and deletes it on shutdown; the MCP server reads the folder and prunes entries of dead processes.
/// Set AUTODESK_MCP_HOME to relocate the folder (used by tests).
/// </summary>
public static class InstanceRegistry
{
    public static string RootDirectory
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable("AUTODESK_MCP_HOME");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutodeskMcp");
        }
    }

    public static string InstancesDirectory => Path.Combine(RootDirectory, "instances");

    public static string Register(InstanceInfo info)
    {
        Directory.CreateDirectory(InstancesDirectory);
        string path = Path.Combine(InstancesDirectory, info.InstanceId + ".json");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(info, BridgeJson.Indented));
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    public static void Unregister(string instanceId)
    {
        try { File.Delete(Path.Combine(InstancesDirectory, instanceId + ".json")); }
        catch { /* best effort */ }
    }

    public static IReadOnlyList<InstanceInfo> ListLive(string? product = null)
    {
        var result = new List<InstanceInfo>();
        if (!Directory.Exists(InstancesDirectory)) return result;

        foreach (string file in Directory.EnumerateFiles(InstancesDirectory, "*.json"))
        {
            InstanceInfo? info = null;
            try { info = JsonSerializer.Deserialize<InstanceInfo>(File.ReadAllText(file), BridgeJson.Options); }
            catch { /* corrupt or being written */ }

            if (info is null) continue;
            if (!IsAlive(info))
            {
                try { File.Delete(file); } catch { }
                continue;
            }
            if (product is null || string.Equals(info.Product, product, StringComparison.OrdinalIgnoreCase))
                result.Add(info);
        }
        return result.OrderBy(i => i.Product).ThenBy(i => i.StartedUtc).ToList();
    }

    /// <summary>
    /// Picks the instance to talk to. With no id and exactly one live instance of the product, that one is used;
    /// otherwise the caller must disambiguate.
    /// </summary>
    public static InstanceInfo Resolve(string product, string? instanceId)
    {
        var live = ListLive(product);
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            return live.FirstOrDefault(i => string.Equals(i.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new BridgeException(
                       $"No running {product} instance with id '{instanceId}'. Live instances: {Describe(live)}");
        }

        return live.Count switch
        {
            0 => throw new BridgeException(
                $"No running {product} instance with the Autodesk MCP add-in was found. " +
                $"Start {product} (the add-in loads automatically) and retry. Registry folder: {InstancesDirectory}"),
            1 => live[0],
            _ => throw new BridgeException(
                $"Several {product} instances are running; pass instanceId. Live instances: {Describe(live)}"),
        };
    }

    private static string Describe(IReadOnlyList<InstanceInfo> list) =>
        list.Count == 0 ? "(none)" : string.Join(", ", list.Select(i => $"{i.InstanceId} ({i.Product} {i.ProductVersion})"));

    private static bool IsAlive(InstanceInfo info)
    {
        try
        {
            using var p = Process.GetProcessById(info.ProcessId);
            if (p.HasExited) return false;
            if (info.ProcessStartTicksUtc == 0) return true;
            // Guards against PID reuse after a crash left a stale file behind.
            long actual = p.StartTime.ToUniversalTime().Ticks;
            return Math.Abs(actual - info.ProcessStartTicksUtc) < TimeSpan.TicksPerSecond * 2;
        }
        catch
        {
            return false;
        }
    }
}
