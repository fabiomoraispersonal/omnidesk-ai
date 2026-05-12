namespace omniDesk.Api.Domain.Pipelines;

// Default 3-column configuration for new pipelines. No magic strings.
public static class PipelineDefaults
{
    public static IReadOnlyList<(string Name, string StatusMapping, int Order)> DefaultColumns =>
    [
        ("Na Fila",            "new",            1),
        ("Em Andamento",       "in_progress",    2),
        ("Aguardando Cliente", "waiting_client", 3),
    ];
}
