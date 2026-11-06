#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AutodeskMcp.Server;
using Xunit;

namespace AutodeskMcp.Tests;

/// <summary>The .mcpb bundle's startup setup: add-ins copied per installed year, settings written to config.json.</summary>
public sealed class AddinSetupTests : System.IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "amcp-setup-" + Guid.NewGuid().ToString("N"));
    private string Bundle => Path.Combine(_root, "bundle");
    private string Addins => Path.Combine(Bundle, "addins");
    private string ProgramFiles => Path.Combine(_root, "Program Files", "Autodesk");
    private string AppData => Path.Combine(_root, "AppData");
    private string ConfigPath => Path.Combine(_root, "Local", "AutodeskMcp", "config.json");

    public AddinSetupTests()
    {
        // Bundle layout as scripts/build.sh writes it.
        Write(Path.Combine(Bundle, "manifest.json"), "{}");
        Write(Path.Combine(Bundle, "server", "AutodeskMcp.Server.exe"), "exe");
        foreach (string y in AddinSetup.Years)
        {
            Write(Path.Combine(Addins, "revit", y, "AutodeskMcp.addin"), $"addin {y}");
            Write(Path.Combine(Addins, "revit", y, "AutodeskMcp", "AutodeskMcp.Revit.dll"), $"revit {y}");
            Write(Path.Combine(Addins, "autocad", "AutodeskMcp.bundle", "Contents", y, "AutodeskMcp.AutoCAD.dll"), $"acad {y}");
        }
        Write(Path.Combine(Addins, "autocad", "AutodeskMcp.bundle", "PackageContents.xml"), "<x/>");

        // This "PC" has Revit 2024 and 2026, and Civil 3D 2025 (it installs under an AutoCAD folder).
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Revit 2024"));
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Revit 2026"));
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "AutoCAD 2025"));
        Directory.CreateDirectory(Path.Combine(ProgramFiles, "Navisworks Manage 2024"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private AddinSetup.SetupResult Run(Dictionary<string, string>? env = null) =>
        AddinSetup.Run(Addins, ProgramFiles, AppData, ConfigPath, k => env is not null && env.TryGetValue(k, out var v) ? v : null, _ => { });

    private string RevitAddins(string year) => Path.Combine(AppData, "Autodesk", "Revit", "Addins", year);
    private string AcadBundle => Path.Combine(AppData, "Autodesk", "ApplicationPlugins", "AutodeskMcp.bundle");

    [Fact]
    public void Finds_the_addins_folder_only_inside_a_bundle()
    {
        Assert.Equal(Addins, AddinSetup.FindAddinsRoot(Path.Combine(Bundle, "server") + Path.DirectorySeparatorChar));
        Assert.Equal(Addins, AddinSetup.FindAddinsRoot(Path.Combine(Bundle, "server")));

        // install.ps1 layout: %LOCALAPPDATA%\AutodeskMcp\server, no manifest or addins beside it.
        string plain = Path.Combine(_root, "Local", "AutodeskMcp", "server");
        Directory.CreateDirectory(plain);
        Assert.Null(AddinSetup.FindAddinsRoot(plain));
    }

    [Fact]
    public void Installs_only_the_years_present_on_the_pc()
    {
        var result = Run();

        Assert.Empty(result.Problems);
        Assert.Equal("revit 2024", File.ReadAllText(Path.Combine(RevitAddins("2024"), "AutodeskMcp", "AutodeskMcp.Revit.dll")));
        Assert.Equal("addin 2026", File.ReadAllText(Path.Combine(RevitAddins("2026"), "AutodeskMcp.addin")));
        Assert.False(Directory.Exists(RevitAddins("2025")));
        Assert.False(Directory.Exists(RevitAddins("2022")));

        // The AutoCAD bundle goes in whole (it serves every year) because an AutoCAD-based product exists.
        Assert.True(File.Exists(Path.Combine(AcadBundle, "PackageContents.xml")));
        Assert.True(File.Exists(Path.Combine(AcadBundle, "Contents", "2025", "AutodeskMcp.AutoCAD.dll")));
        Assert.Equal(3, result.Installed.Count);
        Assert.Contains("restart", result.ToJson()["note"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Second_start_copies_nothing_and_an_update_replaces_changed_files()
    {
        Run();
        var again = Run();
        Assert.Empty(again.Installed);
        Assert.Equal(3, again.UpToDate.Count);
        Assert.Null(again.ToJson()["note"]);

        // A new version of the bundle changes one DLL.
        Write(Path.Combine(Addins, "revit", "2024", "AutodeskMcp", "AutodeskMcp.Revit.dll"), "revit 2024 v2");
        var updated = Run();
        Assert.Single(updated.Installed);
        Assert.Equal("revit 2024 v2", File.ReadAllText(Path.Combine(RevitAddins("2024"), "AutodeskMcp", "AutodeskMcp.Revit.dll")));
    }

    [Fact]
    public void Nothing_is_copied_when_turned_off_in_the_settings()
    {
        var result = Run(new() { [AddinSetup.InstallAddinsVar] = "false" });
        Assert.True(result.AddinsSkipped);
        Assert.False(Directory.Exists(Path.Combine(AppData, "Autodesk")));
    }

    [Fact]
    public void A_file_in_use_is_reported_and_the_rest_continues()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Only Windows locks files that are open.");
        Run();
        Write(Path.Combine(Addins, "revit", "2024", "AutodeskMcp", "AutodeskMcp.Revit.dll"), "revit 2024 v2");
        Write(Path.Combine(Addins, "revit", "2026", "AutodeskMcp", "AutodeskMcp.Revit.dll"), "revit 2026 v2");

        // Revit 2024 is running and has its DLL loaded.
        using (File.Open(Path.Combine(RevitAddins("2024"), "AutodeskMcp", "AutodeskMcp.Revit.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Run();
            Assert.Contains(result.Problems, p => p.StartsWith("Revit 2024", StringComparison.Ordinal) && p.Contains("close Revit 2024"));
            Assert.Contains(result.Installed, s => s.StartsWith("Revit 2026", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Settings_are_merged_into_config_json()
    {
        Write(ConfigPath, "{ \"enabled\": true, \"allowCodeExecution\": true }");

        var result = Run(new()
        {
            [AddinSetup.AllowModificationsVar] = "true",
            [AddinSetup.AllowCodeExecutionVar] = "false",
            [AddinSetup.TimeoutVar] = "90",
        });

        Assert.True(result.ConfigChanged);
        var config = JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();
        Assert.True(config["enabled"]!.GetValue<bool>());
        Assert.True(config["allowModifications"]!.GetValue<bool>());
        Assert.False(config["allowCodeExecution"]!.GetValue<bool>());
        Assert.Equal(90, config["mainThreadTimeoutSeconds"]!.GetValue<int>());

        // The same settings again change nothing.
        Assert.False(Run(new()
        {
            [AddinSetup.AllowModificationsVar] = "true",
            [AddinSetup.AllowCodeExecutionVar] = "false",
            [AddinSetup.TimeoutVar] = "90",
        }).ConfigChanged);
    }

    [Theory]
    [InlineData("${user_config.allow_modifications}")]
    [InlineData("")]
    [InlineData("yes please")]
    public void Unset_or_unknown_values_leave_config_json_alone(string value)
    {
        var result = Run(new() { [AddinSetup.AllowModificationsVar] = value, [AddinSetup.TimeoutVar] = value });
        Assert.False(result.ConfigChanged);
        Assert.False(File.Exists(ConfigPath));
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("45.0", 45)]
    [InlineData("5", null)]
    [InlineData("100000", null)]
    public void Timeout_is_kept_in_range(string value, int? expected) =>
        Assert.Equal(expected, AddinSetup.ParseTimeout(value));
}
#endif
