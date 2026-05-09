using omniDesk.Api.Domain.AiAgents;
using Xunit;

namespace omniDesk.Api.Tests.Domain.AiAgents;

public class HandoffKeywordsTests
{
    [Theory]
    [InlineData("Quero falar com um atendente.", true)]
    [InlineData("Por favor, atendimento humano.", true)]
    [InlineData("Posso falar com o gerente?", true)]
    [InlineData("Preciso do responsável agora", true)]
    [InlineData("Quero falar com alguém da empresa", true)]
    [InlineData("Bom dia, preciso de informações sobre planos.", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Detect_DetectsExpectedKeywords(string input, bool expected)
    {
        Assert.Equal(expected, HandoffKeywords.Detect(input));
    }

    [Theory]
    [InlineData("ATENDENTE")]
    [InlineData("AtEnDeNtE")]
    [InlineData("atêndênte")]   // accents — should still match after normalization
    [InlineData("hUmAnO")]
    public void Detect_IsCaseAndAccentInsensitive(string input)
    {
        Assert.True(HandoffKeywords.Detect(input));
    }
}
