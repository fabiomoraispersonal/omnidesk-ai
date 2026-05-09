using omniDesk.Api.Features.AiAgents.Validators;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiAgents;

public class CreateAiAgentValidatorTests
{
    private readonly CreateAiAgentValidator _v = new();

    [Fact]
    public void Valid_Payload_Passes()
    {
        var req = new CreateAiAgentRequest(
            Name: "Agente Comercial",
            ShortDescription: "Atende vendas, planos e preços.",
            Prompt: "Você é o agente comercial...",
            Model: "gpt-4o",
            DepartmentId: Guid.NewGuid());
        Assert.True(_v.Validate(req).IsValid);
    }

    [Fact]
    public void EmptyName_Fails()
    {
        var req = new CreateAiAgentRequest("", "desc curta", "Prompt longo o suficiente", "gpt-4o", Guid.NewGuid());
        Assert.False(_v.Validate(req).IsValid);
    }

    [Fact]
    public void ShortDescription_Over300_Fails()
    {
        var desc = new string('x', 301);
        var req = new CreateAiAgentRequest("Agente", desc, "Prompt longo o suficiente", "gpt-4o", Guid.NewGuid());
        Assert.False(_v.Validate(req).IsValid);
    }

    [Fact]
    public void Prompt_Under10_Fails()
    {
        var req = new CreateAiAgentRequest("Agente", "desc", "curto", "gpt-4o", Guid.NewGuid());
        Assert.False(_v.Validate(req).IsValid);
    }

    [Fact]
    public void EmptyDepartmentId_Fails()
    {
        var req = new CreateAiAgentRequest("Agente", "desc", "Prompt longo o suficiente", "gpt-4o", Guid.Empty);
        Assert.False(_v.Validate(req).IsValid);
    }
}

public class UpdateAiAgentValidatorTests
{
    private readonly UpdateAiAgentValidator _v = new();

    [Fact]
    public void AllNull_IsValid()
    {
        var req = new UpdateAiAgentRequest(null, null, null, null, null, null);
        Assert.True(_v.Validate(req).IsValid);
    }

    [Fact]
    public void EmptyName_Fails_WhenProvided()
    {
        var req = new UpdateAiAgentRequest("", null, null, null, null, null);
        Assert.False(_v.Validate(req).IsValid);
    }

    [Fact]
    public void Prompt_Min10_Enforced()
    {
        var req = new UpdateAiAgentRequest(null, null, "abc", null, null, null);
        Assert.False(_v.Validate(req).IsValid);
    }
}
