using omniDesk.Api.Features.AgentRuntime;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

/// <summary>
/// FR-013: detector wrapper consome a lista PT-BR estática (HandoffKeywords)
/// e produz hint de sistema bem formado para injeção no thread.
/// </summary>
public class HandoffKeywordDetectorTests
{
    private readonly HandoffKeywordDetector _sut = new();

    [Theory]
    [InlineData("preciso falar com um atendente")]
    [InlineData("Atendente, por favor!")]
    [InlineData("ATENDIMENTO HUMANO já")]
    [InlineData("posso falar com o gerente?")]
    [InlineData("preciso do responsável agora")]
    public void ShouldForceHumanHandoff_DetectsKnownKeywords(string input)
    {
        Assert.True(_sut.ShouldForceHumanHandoff(input));
    }

    [Theory]
    [InlineData("oi, queria saber sobre planos")]
    [InlineData("")]
    [InlineData("muito obrigado pela ajuda")]
    public void ShouldForceHumanHandoff_FalseForRegularMessages(string input)
    {
        Assert.False(_sut.ShouldForceHumanHandoff(input));
    }

    [Fact]
    public void BuildSystemHint_StartsWithSystemMarker()
    {
        var hint = _sut.BuildSystemHint("any user message");
        Assert.StartsWith("[INSTRUÇÃO DO SISTEMA]", hint);
    }

    [Fact]
    public void BuildSystemHint_MentionsTransferTool()
    {
        var hint = _sut.BuildSystemHint("any");
        Assert.Contains("transfer_to_human", hint);
    }

    [Fact]
    public void BuildSystemHint_NotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(_sut.BuildSystemHint("x")));
    }
}
