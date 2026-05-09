using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Features.AiAgents.Variables;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

public class PromptVariableSubstitutorTests
{
    private readonly PromptVariableSubstitutor _sub = new(NullLogger<PromptVariableSubstitutor>.Instance);

    [Fact]
    public void Apply_ReplacesKnownVariables()
    {
        var prompt = "Olá da {{company_name}}, depto {{department_name}}, atendente {{attendant_name}}.";
        var result = _sub.Apply(prompt, new AgentVariablesContext("Clínica Beta", "Comercial", "Maria"));
        Assert.Equal("Olá da Clínica Beta, depto Comercial, atendente Maria.", result);
    }

    [Fact]
    public void Apply_LeavesUnknownVariablesLiteral()
    {
        var prompt = "Hello {{customer_xpto}}!";
        var result = _sub.Apply(prompt, new AgentVariablesContext(null, null, null));
        Assert.Equal("Hello {{customer_xpto}}!", result);
    }

    [Fact]
    public void Apply_ReplacesMissingValuesWithEmptyString()
    {
        var prompt = "[{{company_name}}][{{department_name}}][{{attendant_name}}]";
        var result = _sub.Apply(prompt, new AgentVariablesContext(null, null, null));
        Assert.Equal("[][][]", result);
    }

    [Fact]
    public void Apply_HandlesEmptyPrompt()
    {
        var result = _sub.Apply(string.Empty, new AgentVariablesContext("X", "Y", "Z"));
        Assert.Equal(string.Empty, result);
    }
}
