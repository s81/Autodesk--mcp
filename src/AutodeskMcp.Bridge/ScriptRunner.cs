using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif

namespace AutodeskMcp.Bridge;

/// <summary>
/// Loads an assembly that the MCP server compiled (Roslyn runs out-of-process, so the host never
/// loads Roslyn and cannot get version conflicts from it) and invokes it.
/// On .NET 8+ the script goes into a collectible AssemblyLoadContext that is unloaded afterwards.
/// On .NET Framework 4.8 (Revit/AutoCAD 2022-2024) assemblies cannot be unloaded, so each executed script
/// stays in memory (a few KB) until the host application closes.
/// </summary>
public static class ScriptRunner
{
    public const string EntryType = "McpScript";
    public const string EntryMethod = "Run";
    public const string AssemblyNamePrefix = "McpScript_";

    public static bool IsScriptAssembly(Assembly asm) =>
        asm.GetName().Name?.StartsWith(AssemblyNamePrefix, StringComparison.Ordinal) == true;

    public static object? Run(byte[] assembly, object?[] args)
    {
#if NETFRAMEWORK
        ResolveEventHandler resolver = (_, e) => FindLoaded(new AssemblyName(e.Name));
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            return Invoke(Assembly.Load(assembly), args);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
#else
        var context = new ScriptLoadContext();
        try
        {
            using var stream = new MemoryStream(assembly, writable: false);
            return Invoke(context.LoadFromStream(stream), args);
        }
        finally
        {
            // Collected once nothing references the script's types any more.
            context.Unload();
        }
#endif
    }

    private static object? Invoke(Assembly asm, object?[] args)
    {
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

    /// <summary>The highest version of an already-loaded assembly with this simple name (any load context).</summary>
    private static Assembly? FindLoaded(AssemblyName name)
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
        return best;
    }

    /// <summary>
    /// Every assembly the script may reference: everything the host has loaded (Revit/AutoCAD API, WPF, ...)
    /// plus the runtime's framework assemblies. Returned to the MCP server for compilation.
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
            if (!byName.ContainsKey(name)) byName[name] = location;
        }

#if NETFRAMEWORK
        // e.g. C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ (mscorlib, System.*, netstandard facade, ...)
        // Listed only, not opened: checking each of these hundreds of files here was slow enough to exceed the
        // client's 30 s timeout once in CI. The directory also holds native DLLs and manifest-less modules
        // (e.g. System.EnterpriseServices.Wrapper.dll); the MCP server drops those before compiling (CS1509).
        string runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (string path in SafeEnumerate(runtimeDir, "*.dll").Concat(SafeEnumerate(Path.Combine(runtimeDir, "WPF"), "*.dll")))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!byName.ContainsKey(name)) byName[name] = path;
        }
#else
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string path in tpa.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!byName.ContainsKey(name)) byName[name] = path;
            }
        }
#endif

        return byName.Values.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

#if NETFRAMEWORK
    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
#else
    private sealed class ScriptLoadContext : AssemblyLoadContext
    {
        public ScriptLoadContext() : base("AutodeskMcp.Script", isCollectible: true) { }

        // Bind script references to the assemblies the host already loaded, whichever load context
        // they live in (Revit 2026+ isolates add-ins in their own contexts).
        protected override Assembly? Load(AssemblyName name) => FindLoaded(name); // null -> default probing
    }
#endif
}
