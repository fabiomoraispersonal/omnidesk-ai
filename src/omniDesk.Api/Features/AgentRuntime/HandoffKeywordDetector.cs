using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Features.AgentRuntime;

public class HandoffKeywordDetector
{
    public bool ShouldForceHumanHandoff(string clientMessage) => HandoffKeywords.Detect(clientMessage);

    public string BuildSystemHint(string clientMessage)
        => "[INSTRUÇÃO DO SISTEMA] O cliente solicitou explicitamente um atendente humano. Execute imediatamente a tool `transfer_to_human` informando o departamento adequado e mensagem cordial de transferência.";
}
