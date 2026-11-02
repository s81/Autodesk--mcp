using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace AutodeskMcp.Bridge;

/// <summary>
/// Loads an assembly that the MCP server compiled (Roslyn runs out-of-process, so the host never
/// loads Roslyn and cannot get version conflicts from it) into a collectible load context and invokes it.
/// </summary>
public static class ScriptRunner
{
    public const string EntryType = "McpScript";
    public const string EntryMethod = "Run";

    public static object? Run(byte[] assembly, object?[] args)
    {
        var context = new ScriptLoadContext();
        try
        {
            using var stream = new MemoryStream(assembly, writable: false);
            Assembly asm = context.LoadFromStream(stream);
            Type type = asm.GetType(EntryType, throwOnError: true)!;
            MethodInfo method = type.GetMethod(EntryMethod, BindingFlags.Public | BindingFlags.Static)
                                ?? throw new BridgeException($"Script has no public static {EntryType}.{EntryMethod}.");
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw; // unreachable
            }
        }
        finally
        {
            // Collected once nothing references the script's types any more.
            context.Unload();
        }
    }

    /// <summary>
    /// Every assembly the script may reference: the runtime's trusted platform assemblies plus everything
    /// the host has loaded (Revit/AutoCAD API, WPF, etc.). Returned to the MCP server for compilation.
    /// </summary>
    public static IReadOnlyList<string> GetReferencePaths()
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            string location;
#pragma warning disable IL3000 // Runs inside Revit/AutoCAD (never single-file); the server never calls this.
            try { location = asm.Location; } catch { continue; }
#pragma warning restore IL3000
            if (string.IsNullOrEmpty(location)) continue;
            string? name = asm.GetName().Name;
            if (name is null || name.StartsWith("AutodeskMcp", StringComparison.OrdinalIgnoreCase)) continue;
            byName.TryAdd(name, location);
        }

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                byName.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        return byName.Values.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class ScriptLoadContext : AssemblyLoadContext
    {
        public ScriptLoadContext() : base("AutodeskMcp.Script", isCollectible: true) { }

        // Bind script references to the assemblies the host already loaded, whichever load context
        // they live in (Revit 2026+ isolates add-ins in their own contexts).
        protected override Assembly? Load(AssemblyName name)
        {
            Assembly? best = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var candidate = asm.GetName();
                if (!string.Equals(candidate.Name, name.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (best is null || (candidate.Version ?? new Version()) > (best.GetName().Version ?? new Version()))
                    best = asm;
            }
            return best; // null → default context probing
        }
    }
}
