using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AutodeskMcp.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModelContextProtocol;

namespace AutodeskMcp.Server;

public sealed record CompileResult(byte[]? Assembly, IReadOnlyList<string> Errors)
{
    public bool Success => Assembly is not null;
}

/// <summary>
/// Compiles execute_csharp snippets here, in the MCP server process, against the exact assemblies the
/// target host has loaded (paths obtained from the add-in). The add-in only receives finished IL bytes.
/// </summary>
public sealed class ScriptCompiler(HostGateway gateway)
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<MetadataReference>> _references = new();

    public static readonly string[] RevitUsings =
    {
        "System", "System.Collections.Generic", "System.Linq", "System.Text",
        "Autodesk.Revit.DB", "Autodesk.Revit.UI",
    };

    // Autodesk.AutoCAD.Runtime is deliberately not imported: its Exception type clashes with System.Exception.
    public static readonly string[] AutoCADUsings =
    {
        "System", "System.Collections.Generic", "System.Linq", "System.Text",
        "Autodesk.AutoCAD.ApplicationServices", "Autodesk.AutoCAD.DatabaseServices",
        "Autodesk.AutoCAD.EditorInput", "Autodesk.AutoCAD.Geometry",
    };

    public static string WrapRevit(string code, IEnumerable<string>? extraUsings) => Wrap(
        RevitUsings, extraUsings,
        "Autodesk.Revit.UI.UIApplication uiapp, System.Text.StringBuilder output",
        """
                var app = uiapp.Application;
                var uidoc = uiapp.ActiveUIDocument;
                var doc = uidoc?.Document;
        """,
        code);

    public static string WrapAutoCAD(string code, IEnumerable<string>? extraUsings) => Wrap(
        AutoCADUsings, extraUsings,
        "Autodesk.AutoCAD.ApplicationServices.Document doc, Autodesk.AutoCAD.DatabaseServices.Transaction tr, System.Text.StringBuilder output",
        """
                var db = doc.Database;
                var ed = doc.Editor;
        """,
        code);

    public static string Wrap(IEnumerable<string> usings, IEnumerable<string>? extraUsings, string parameters, string prologue, string code)
    {
        var sb = new StringBuilder();
        foreach (string u in usings.Concat(extraUsings ?? Array.Empty<string>()).Select(NormalizeUsing).Distinct())
            sb.Append("using ").Append(u).AppendLine(";");
        sb.AppendLine("#pragma warning disable");
        sb.AppendLine($"public static class {ScriptRunner.EntryType}");
        sb.AppendLine("{");
        sb.AppendLine($"    public static object {ScriptRunner.EntryMethod}({parameters})");
        sb.AppendLine("    {");
        sb.AppendLine(prologue);
        sb.AppendLine("#line 1 \"script.cs\"");
        sb.AppendLine(code);
        sb.AppendLine("#line default");
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string NormalizeUsing(string u)
    {
        u = u.Trim();
        if (u.StartsWith("using ", StringComparison.Ordinal)) u = u[6..];
        u = u.TrimEnd(';').Trim();
        // Allow aliases ("Foo = Bar.Baz") and "static X"; reject anything that could inject code.
        if (u.Length == 0 || u.IndexOfAny(new[] { ';', '{', '}', '(', ')', '\n', '\r', '"' }) >= 0)
            throw new McpException($"Invalid using directive '{u}'.");
        return u;
    }

    public static CompileResult Compile(string source, IEnumerable<MetadataReference> references)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "McpScript_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, // accurate line numbers in exception stack traces
                platform: Platform.AnyCpu,
                nullableContextOptions: NullableContextOptions.Disable));

        using var ms = new MemoryStream();
        // Embedded PDB: runtime exceptions report 'script.cs:line N' of the user's snippet.
        var emit = compilation.Emit(ms, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.Embedded));
        if (emit.Success) return new CompileResult(ms.ToArray(), Array.Empty<string>());

        var errors = emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d =>
            {
                var span = d.Location.GetMappedLineSpan();
                string where = span.Path == "script.cs" ? $"line {span.StartLinePosition.Line + 1}, col {span.StartLinePosition.Character + 1}" : "wrapper";
                return $"{d.Id} ({where}): {d.GetMessage()}";
            })
            .Distinct()
            .Take(30)
            .ToList();
        return new CompileResult(null, errors);
    }

    public static IReadOnlyList<MetadataReference> CreateReferences(IEnumerable<string> paths)
    {
        var list = new List<MetadataReference>();
        foreach (string path in paths)
        {
            if (!File.Exists(path) || !HasMetadata(path)) continue;
            list.Add(MetadataReference.CreateFromFile(path));
        }
        return list;
    }

    private static bool HasMetadata(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            return pe.HasMetadata;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Compiles <paramref name="source"/> against what <paramref name="instance"/> has loaded.</summary>
    public async Task<CompileResult> CompileForAsync(InstanceInfo instance, string source, CancellationToken ct)
    {
        // Re-fetch on each call: hosts load more assemblies over time (e.g. after opening a family editor).
        var node = await gateway.CallNodeAsync(instance, "__script_references", null, ct);
        var paths = (node as JsonArray)?.Select(n => n?.ToString() ?? "").Where(p => p.Length > 0).ToList()
                    ?? throw new McpException("Add-in returned no reference list.");

        string key = instance.InstanceId + "|" + string.Join("|", paths).GetHashCode();
        var refs = _references.GetOrAdd(key, _ => CreateReferences(paths));
        return Compile(source, refs);
    }

    public static string FormatErrors(CompileResult r) =>
        "Compilation failed:\n" + string.Join("\n", r.Errors);
}
