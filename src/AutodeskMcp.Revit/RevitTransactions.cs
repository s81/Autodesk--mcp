using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using AutodeskMcp.Bridge;
using Autodesk.Revit.DB;

namespace AutodeskMcp.Revit;

/// <summary>
/// Collects warnings instead of showing Revit's warning dialogs (which would block the API thread) and turns
/// errors into a rollback with a readable message.
/// </summary>
internal sealed class FailureCollector : IFailuresPreprocessor
{
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        bool hasErrors = false;
        foreach (var failure in accessor.GetFailureMessages())
        {
            string text = failure.GetDescriptionText();
            if (failure.GetSeverity() == FailureSeverity.Warning)
            {
                Warnings.Add(text);
                accessor.DeleteWarning(failure);
            }
            else
            {
                Errors.Add(text);
                hasErrors = true;
            }
        }
        return hasErrors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}

internal static class RevitTransactions
{
    /// <summary>
    /// Runs <paramref name="body"/> in a named transaction (one Undo step). Any exception rolls back.
    /// The returned object has "result" and, if any, "warnings".
    /// </summary>
    public static JsonObject Run(Document doc, string name, Func<JsonNode?> body)
    {
        if (doc.IsReadOnly) throw new BridgeException("The active document is read-only.");
        if (doc.IsModifiable) throw new BridgeException("The document already has an open transaction; finish the active edit in Revit first.");

        var failures = new FailureCollector();
        using var transaction = new Transaction(doc, name);
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(failures);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);

        transaction.Start();
        JsonNode? result;
        try
        {
            result = body();
        }
        catch
        {
            if (transaction.HasStarted() && !transaction.HasEnded()) transaction.RollBack();
            throw;
        }

        var status = transaction.Commit();
        if (status != TransactionStatus.Committed)
        {
            string why = failures.Errors.Count > 0 ? string.Join("; ", failures.Errors) : status.ToString();
            throw new BridgeException($"Transaction '{name}' was rolled back: {why}");
        }

        var response = new JsonObject { ["result"] = result };
        if (failures.Warnings.Count > 0)
            response["warnings"] = new JsonArray(failures.Warnings.ConvertAll(w => (JsonNode?)w).ToArray());
        return response;
    }
}
