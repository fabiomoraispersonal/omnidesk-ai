using System.Text.RegularExpressions;
using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Features.AiAgents.Variables;

public class PromptVariableSubstitutor
{
    private static readonly Regex Pattern = new(@"\{\{(?<name>\w+)\}\}", RegexOptions.Compiled);

    private readonly ILogger<PromptVariableSubstitutor> _logger;

    public PromptVariableSubstitutor(ILogger<PromptVariableSubstitutor> logger) => _logger = logger;

    public string Apply(string prompt, AgentVariablesContext ctx)
    {
        if (string.IsNullOrEmpty(prompt)) return prompt;

        return Pattern.Replace(prompt, match =>
        {
            var name = match.Groups["name"].Value;
            if (!AgentVariableNames.Known.Contains(name))
            {
                _logger.LogWarning("Unknown prompt variable {{Name}} in agent prompt — leaving literal.", name);
                return match.Value;
            }

            return name.ToLowerInvariant() switch
            {
                AgentVariableNames.CompanyName => ctx.CompanyName ?? string.Empty,
                AgentVariableNames.DepartmentName => ctx.DepartmentName ?? string.Empty,
                AgentVariableNames.AttendantName => ctx.AttendantName ?? string.Empty,
                _ => match.Value,
            };
        });
    }
}

public record AgentVariablesContext(
    string? CompanyName,
    string? DepartmentName,
    string? AttendantName);
