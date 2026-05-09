using System.Text.Json;
using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Infrastructure.OpenAi;

public static class OpenAiToolDefinitions
{
    public static IReadOnlyList<object> All() => new[]
    {
        Build(ToolNames.HandoffToAgent,
            "Transfere a conversa para outro agente (Orchestrator ou Sub-agente) quando identificada mudança de contexto ou intenção.",
            new
            {
                type = "object",
                properties = new
                {
                    agent_id = new
                    {
                        type = "string",
                        description = "UUID do agente de destino (deve estar ativo no tenant). Use 'orchestrator' como atalho para devolver ao Orchestrator.",
                    },
                    reason = new
                    {
                        type = "string",
                        description = "Motivo da transferência. Interno, não enviado ao cliente.",
                    },
                },
                required = new[] { "agent_id", "reason" },
            }),
        Build(ToolNames.TransferToHuman,
            "Transfere a conversa para um atendente humano e abre um ticket no departamento correto. Após esta tool, a IA não processa mais mensagens nesta conversa.",
            new
            {
                type = "object",
                properties = new
                {
                    department_id = new
                    {
                        type = "string",
                        description = "UUID do departamento. Para o Orchestrator, pode ser omitido — sistema usa departamento padrão do tenant.",
                    },
                    reason = new
                    {
                        type = "string",
                        description = "Motivo do transbordo. Registrado no ticket.",
                    },
                },
                required = new[] { "reason" },
            }),
        Build(ToolNames.CheckAvailability,
            "Consulta horários disponíveis na agenda do tenant.",
            new
            {
                type = "object",
                properties = new
                {
                    professional_id = new { type = "string" },
                    date = new { type = "string", description = "YYYY-MM-DD" },
                },
                required = new[] { "professional_id", "date" },
            }),
        Build(ToolNames.CreateAppointment,
            "Cria um agendamento para o cliente após confirmação.",
            new
            {
                type = "object",
                properties = new
                {
                    professional_id = new { type = "string" },
                    datetime = new { type = "string" },
                    client_name = new { type = "string" },
                    client_phone = new { type = "string" },
                },
                required = new[] { "professional_id", "datetime", "client_name", "client_phone" },
            }),
    };

    private static object Build(string name, string description, object parameters) => new
    {
        type = "function",
        function = new
        {
            name,
            description,
            parameters,
        },
    };

    public static string SerializeAll() => JsonSerializer.Serialize(All());
}
