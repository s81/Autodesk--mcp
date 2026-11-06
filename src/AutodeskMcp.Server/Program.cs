using System;
using AutodeskMcp.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Installed from the .mcpb bundle: put the add-ins in place and apply the extension settings before serving.
AddinSetup.RunAtStartup(message => Console.Error.WriteLine(message));

var builder = Host.CreateApplicationBuilder(args);

// stdout is the MCP channel: every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton<HostGateway>();
builder.Services.AddSingleton<ScriptCompiler>();

builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "autodesk-mcp", Version = typeof(HostGateway).Assembly.GetName().Version?.ToString(3) ?? "0.1.0" };
        o.ServerInstructions =
            "Tools for live Autodesk Revit and AutoCAD sessions (2022-2027) running on this computer. " +
            "Call list_autodesk_instances first. Revit internal units are decimal feet and radians; tools that take " +
            "lengths require an explicit 'units' argument. AutoCAD coordinates are in drawing units (see INSUNITS in " +
            "autocad_get_session_info). Prefer the specific tools; use *_execute_csharp only when no tool fits.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
