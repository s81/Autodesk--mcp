using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AutodeskMcp.Bridge;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: Autodesk.AutoCAD.Runtime.ExtensionApplication(typeof(AutodeskMcp.AutoCAD.BridgeExtension))]

namespace AutodeskMcp.AutoCAD;

/// <summary>AutoCAD entry point: starts the named-pipe bridge and registers this AutoCAD process for discovery.</summary>
public sealed class BridgeExtension : Autodesk.AutoCAD.Runtime.IExtensionApplication
{
    private BridgeHost? _host;
    private InstanceInfo? _info;

    public void Initialize()
    {
        try
        {
            var config = BridgeConfig.Load(Log);
            if (!config.Enabled)
            {
                Log("Disabled by config.json (enabled=false).");
                return;
            }

            using var process = Process.GetCurrentProcess();
            string year = ProductYear(AcApp.Version);
            _info = new InstanceInfo
            {
                InstanceId = $"autocad-{year}-{process.Id}",
                Product = "autocad",
                ProductVersion = year,
                ProductBuild = $"{AcApp.GetSystemVariable("PRODUCT")} {AcApp.Version}",
                AddinVersion = typeof(BridgeExtension).Assembly.GetName().Version?.ToString(3) ?? "",
                RuntimeVersion = RuntimeInformation.FrameworkDescription,
                ProcessId = process.Id,
                ProcessStartTicksUtc = process.StartTime.ToUniversalTime().Ticks,
                PipeName = $"autodesk-mcp-autocad-{process.Id}-{Guid.NewGuid():N}",
                Token = BridgeHost.NewToken(),
                StartedUtc = DateTimeOffset.UtcNow,
            };

            // Initialize runs on AutoCAD's main thread, so the dispatcher's window handle belongs to it.
            var dispatcher = new AcadDispatcher();
            _host = new BridgeHost(_info, config, dispatcher, AcadCommands.Create(), Log);
            _host.Start();
            InstanceRegistry.Register(_info);
            Log($"Ready: {_info.InstanceId}");
        }
        catch (Exception ex)
        {
            Log("Initialize failed: " + ex);
        }
    }

    public void Terminate()
    {
        if (_info is not null) InstanceRegistry.Unregister(_info.InstanceId);
        _host?.Dispose();
    }

    /// <summary>R25.0 = 2025, R25.1 = 2026, R26.0 = 2027.</summary>
    internal static string ProductYear(Version v) => (v.Major, v.Minor) switch
    {
        (25, 0) => "2025",
        (25, 1) => "2026",
        (26, 0) => "2027",
        _ => $"R{v.Major}.{v.Minor}",
    };

    internal static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(InstanceRegistry.RootDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"autocad-{Environment.ProcessId}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
