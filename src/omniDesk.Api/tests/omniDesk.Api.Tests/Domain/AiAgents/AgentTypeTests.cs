using omniDesk.Api.Domain.AiAgents;
using Xunit;

namespace omniDesk.Api.Tests.Domain.AiAgents;

public class AgentTypeTests
{
    [Theory]
    [InlineData("orchestrator", AgentType.Orchestrator)]
    [InlineData("sub_agent", AgentType.SubAgent)]
    public void Parse_KnownValues(string wire, AgentType expected)
        => Assert.Equal(expected, AgentTypes.Parse(wire));

    [Fact]
    public void Parse_UnknownThrows()
        => Assert.Throws<ArgumentException>(() => AgentTypes.Parse("admin"));

    [Theory]
    [InlineData(AgentType.Orchestrator, "orchestrator")]
    [InlineData(AgentType.SubAgent, "sub_agent")]
    public void ToWire_RoundTrips(AgentType type, string expected)
        => Assert.Equal(expected, AgentTypes.ToWire(type));
}
