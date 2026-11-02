using System;
using System.IO;
using System.Text.Json;

namespace AutodeskMcp.Bridge;

/// <summary>
/// Per-user policy read by the host add-in at startup from %LOCALAPPDATA%\AutodeskMcp\config.json.
/// The add-in enforces it, so an MCP client cannot bypass it.
/// </summary>
public sealed class BridgeConfig
{
    /// <summary>When false the add-in does not open the pipe at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When false every tool that changes the model/drawing is rejected.</summary>
    public bool AllowModifications { get; set; } = true;

    /// <summary>When false the execute_csharp tools are rejected.</summary>
    public bool AllowCodeExecution { get; set; } = true;

    /// <summary>How long a request may wait for the host's main thread before it is cancelled (it then never runs).</summary>
    public int MainThreadTimeoutSeconds { get; set; } = 120;

    public static string FilePath => Path.Combine(InstanceRegistry.RootDirectory, "config.json");

    public static BridgeConfig Load(Action<string>? log = null)
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BridgeConfig>(File.ReadAllText(FilePath), BridgeJson.Options) ?? new BridgeConfig();
        }
        catch (Exception ex)
        {
            log?.Invoke($"Could not read {FilePath}; using defaults. {ex.Message}");
        }
        return new BridgeConfig();
    }
}
