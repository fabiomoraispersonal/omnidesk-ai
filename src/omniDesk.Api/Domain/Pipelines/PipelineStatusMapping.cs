namespace omniDesk.Api.Domain.Pipelines;

// Validates that a pipeline column set is well-formed.
public static class PipelineStatusMapping
{
    private static readonly HashSet<string> ValidMappings = ["new", "in_progress", "waiting_client"];

    public static (bool IsValid, string? Error) Validate(IReadOnlyList<PipelineColumn> columns)
    {
        if (columns.Count != 3)
            return (false, $"Pipeline must have exactly 3 columns, got {columns.Count}.");

        var mappings = columns.Select(c => c.StatusMapping).ToList();

        if (mappings.Any(m => !ValidMappings.Contains(m)))
            return (false, $"Invalid status_mapping value. Allowed: {string.Join(", ", ValidMappings)}.");

        if (mappings.Distinct().Count() != 3)
            return (false, "Each status_mapping must be unique across columns.");

        return (true, null);
    }
}
