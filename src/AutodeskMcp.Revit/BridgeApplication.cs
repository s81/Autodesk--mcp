using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AutodeskMcp.Bridge;
using Autodesk.Revit.UI;

namespace AutodeskMcp.Revit;

/// <summary>Revit entry point: starts the named-pipe bridge and registers this Revit process for discovery.</summary>
public sealed class BridgeApplication : IExternalApplication
{
    private BridgeHost? _host;
    private InstanceInfo? _info;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            var config = BridgeConfig.Load(Log);
            if (!config.Enabled)
            {
                Log("Disabled by config.json (enabled=false).");
                return Result.Succeeded;
            }

            using var process = Process.GetCurrentProcess();
            string year = application.ControlledApplication.VersionNumber;
            _info = new InstanceInfo
            {
                InstanceId = $"revit-{year}-{process.Id}",
                Product = "revit",
                ProductVersion = year,
                ProductBuild = application.ControlledApplication.VersionBuild,
                AddinVersion = typeof(BridgeApplication).Assembly.GetName().Version?.ToString(3) ?? "",
                RuntimeVersion = RuntimeInformation.FrameworkDescription,
                ProcessId = process.Id,
                ProcessStartTicksUtc = process.StartTime.ToUniversalTime().Ticks,
                PipeName = $"autodesk-mcp-revit-{process.Id}-{Guid.NewGuid():N}",
                Token = BridgeHost.NewToken(),
                StartedUtc = DateTimeOffset.UtcNow,
            };

            // ExternalEvent.Create must be called in a valid API context; OnStartup is one.
            var dispatcher = new RevitDispatcher();
            _host = new BridgeHost(_info, config, dispatcher, RevitCommands.Create(dispatcher), Log);
            _host.Start();
            InstanceRegistry.Register(_info);
            Log($"Ready: {_info.InstanceId}");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            Log("Startup failed: " + ex);
            // Do not block Revit startup because of the bridge.
            return Result.Succeeded;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        if (_info is not null) InstanceRegistry.Unregister(_info.InstanceId);
        _host?.Dispose();
        return Result.Succeeded;
    }

    internal static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(InstanceRegistry.RootDirectory, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"revit-{Environment.ProcessId}.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
