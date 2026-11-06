using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutodeskMcp.Bridge;

namespace AutodeskMcp.Server;

/// <summary>
/// One-click setup for the .mcpb bundle. Claude Desktop installs the bundle as a folder holding the server
/// (server/AutodeskMcp.Server.exe) and the add-ins (addins/revit/&lt;year&gt;, addins/autocad/AutodeskMcp.bundle).
/// On every start the server:
/// <list type="bullet">
/// <item>copies the add-ins into the per-user Revit/AutoCAD folders for each year installed on this PC, doing the
/// same as scripts/install.ps1 and copying only files that differ;</item>
/// <item>writes the policy chosen in Claude Desktop's extension settings to %LOCALAPPDATA%\AutodeskMcp\config.json,
/// where the add-ins read and enforce it.</item>
/// </list>
/// Without an addins folder next to the exe (installed with install.ps1), nothing is copied.
/// </summary>
internal static class AddinSetup
{
    internal static readonly string[] Years = { "2022", "2023", "2024", "2025", "2026", "2027" };

    internal const string InstallAddinsVar = "AUTODESK_MCP_INSTALL_ADDINS";
    internal const string AllowModificationsVar = "AUTODESK_MCP_ALLOW_MODIFICATIONS";
    internal const string AllowCodeExecutionVar = "AUTODESK_MCP_ALLOW_CODE_EXECUTION";
    internal const string TimeoutVar = "AUTODESK_MCP_TIMEOUT_SECONDS";

    /// <summary>What the last <see cref="RunAtStartup"/> did; null when it did not run.</summary>
    public static SetupResult? Last { get; private set; }

    public sealed class SetupResult
    {
        public List<string> Installed { get; } = new();
        public List<string> UpToDate { get; } = new();
        public List<string> Problems { get; } = new();
        public bool AddinsSkipped { get; set; }
        public bool ConfigChanged { get; set; }

        public JsonObject ToJson()
        {
            var o = new JsonObject
            {
                ["installedOrUpdated"] = new JsonArray(Installed.Select(s => (JsonNode?)s).ToArray()),
                ["upToDate"] = new JsonArray(UpToDate.Select(s => (JsonNode?)s).ToArray()),
            };
            if (AddinsSkipped) o["addinsSkipped"] = "turned off in the extension settings";
            if (Problems.Count > 0) o["problems"] = new JsonArray(Problems.Select(s => (JsonNode?)s).ToArray());
            if (Installed.Count > 0 || ConfigChanged)
                o["note"] = "Restart Revit/AutoCAD to load the new add-ins or settings.";
            return o;
        }
    }

    public static void RunAtStartup(Action<string> log)
    {
        try
        {
            // The bundle keeps the exe in server/ and the add-ins beside that folder, in addins/.
            string? addinsRoot = FindAddinsRoot(AppContext.BaseDirectory);
            if (addinsRoot is null) return;
            Last = Run(
                addinsRoot,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                BridgeConfig.FilePath,
                Environment.GetEnvironmentVariable,
                log);
        }
        catch (Exception ex)
        {
            log($"Add-in setup failed: {ex.Message}");
        }
    }

    /// <summary>The bundle's addins folder for an exe in &lt;bundle&gt;\server\, or null outside a bundle.</summary>
    internal static string? FindAddinsRoot(string exeDirectory)
    {
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(exeDirectory));
        if (parent is null) return null;
        string candidate = Path.Combine(parent, "addins");
        return Directory.Exists(candidate) && File.Exists(Path.Combine(parent, "manifest.json")) ? candidate : null;
    }

    /// <param name="programFilesAutodesk">e.g. C:\Program Files\Autodesk (installed products are detected here)</param>
    /// <param name="appData">%APPDATA% (Revit\Addins and ApplicationPlugins live under it)</param>
    internal static SetupResult Run(string addinsRoot, string programFilesAutodesk, string appData, string configPath,
        Func<string, string?> env, Action<string> log)
    {
        var result = new SetupResult();
        ApplyPolicy(configPath, env, result);

        if (ParseBool(env(InstallAddinsVar)) == false)
        {
            result.AddinsSkipped = true;
        }
        else
        {
            InstallRevit(addinsRoot, programFilesAutodesk, appData, result);
            InstallAutoCAD(addinsRoot, programFilesAutodesk, appData, result);
        }

        foreach (string s in result.Installed) log($"Add-in installed/updated: {s}");
        foreach (string s in result.Problems) log($"Add-in setup problem: {s}");
        if (result.ConfigChanged) log($"Policy written to {configPath}");
        return result;
    }

    private static void InstallRevit(string addinsRoot, string programFilesAutodesk, string appData, SetupResult result)
    {
        foreach (string year in Years)
        {
            string src = Path.Combine(addinsRoot, "revit", year);
            if (!Directory.Exists(src) || !Directory.Exists(Path.Combine(programFilesAutodesk, $"Revit {year}"))) continue;
            string dest = Path.Combine(appData, "Autodesk", "Revit", "Addins", year);
            Sync($"Revit {year}", src, dest, result, $"close Revit {year}, then restart Claude");
        }
    }

    private static void InstallAutoCAD(string addinsRoot, string programFilesAutodesk, string appData, SetupResult result)
    {
        string src = Path.Combine(addinsRoot, "autocad", "AutodeskMcp.bundle");
        if (!Directory.Exists(src)) return;

        // AutoCAD and its verticals (Civil 3D, Plant 3D, ...) share one bundle that holds every year.
        string[] productDirs = SafeDirectories(programFilesAutodesk);
        var years = Years.Where(y => productDirs.Any(d =>
                Regex.IsMatch(Path.GetFileName(d), $"^AutoCAD.* {y}$", RegexOptions.IgnoreCase)))
            .ToList();
        if (years.Count == 0) return;

        string dest = Path.Combine(appData, "Autodesk", "ApplicationPlugins", "AutodeskMcp.bundle");
        Sync($"AutoCAD {string.Join(", ", years)}", src, dest, result, "close AutoCAD, then restart Claude");
    }

    private static void Sync(string label, string src, string dest, SetupResult result, string whenLocked)
    {
        try
        {
            int changed = 0;
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(dest, Path.GetRelativePath(src, file));
                if (SameContent(file, target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                changed++;
            }
            (changed > 0 ? result.Installed : result.UpToDate).Add($"{label} -> {dest}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Typically the program is running and has the old DLL loaded.
            result.Problems.Add($"{label}: {ex.Message} ({whenLocked}).");
        }
    }

    private static bool SameContent(string a, string b)
    {
        var fb = new FileInfo(b);
        if (!fb.Exists || fb.Length != new FileInfo(a).Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    private static string[] SafeDirectories(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Merges the settings Claude Desktop passes as environment variables into config.json.</summary>
    private static void ApplyPolicy(string configPath, Func<string, string?> env, SetupResult result)
    {
        bool? allowModifications = ParseBool(env(AllowModificationsVar));
        bool? allowCode = ParseBool(env(AllowCodeExecutionVar));
        int? timeout = ParseTimeout(env(TimeoutVar));
        if (allowModifications is null && allowCode is null && timeout is null) return;

        try
        {
            JsonObject config;
            try
            {
                config = File.Exists(configPath) ? JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new() : new();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException)
            {
                result.Problems.Add($"{configPath} was not valid JSON and was replaced: {ex.Message}");
                config = new JsonObject();
            }

            bool changed = false;
            void Set(string key, JsonNode value)
            {
                if (JsonNode.DeepEquals(config[key], value)) return;
                config[key] = value;
                changed = true;
            }
            if (allowModifications is bool m) Set("allowModifications", JsonValue.Create(m));
            if (allowCode is bool c) Set("allowCodeExecution", JsonValue.Create(c));
            if (timeout is int t) Set("mainThreadTimeoutSeconds", JsonValue.Create(t));
            if (!changed) return;

            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            string tmp = configPath + ".tmp";
            File.WriteAllText(tmp, config.ToJsonString(BridgeJson.Indented));
            File.Move(tmp, configPath, overwrite: true);
            result.ConfigChanged = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Problems.Add($"Could not write {configPath}: {ex.Message}");
        }
    }

    // Claude Desktop passes booleans as "true"/"false"; anything else (unset, or a placeholder a client
    // did not substitute) means "leave as is".
    internal static bool? ParseBool(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null,
    };

    internal static int? ParseTimeout(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
        && d >= 10 && d <= 3600
            ? (int)Math.Round(d)
            : null;
}
