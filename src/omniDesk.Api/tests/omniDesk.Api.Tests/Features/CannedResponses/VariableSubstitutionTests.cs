using System.Diagnostics;
using omniDesk.Api.Features.CannedResponses;
using Xunit;

namespace omniDesk.Api.Tests.Features.CannedResponses;

public class VariableSubstitutionTests
{
    [Fact]
    public void SubstitutesAllFourCanonicalVariables()
    {
        var template = "Olá {{client_name}}, sou {{attendant_name}} do {{department_name}}. Ticket #{{ticket_number}}.";
        var ctx = new SubstitutionContext("Maria", "Carlos", 4321, "Comercial");
        var result = VariableSubstitution.Apply(template, ctx);
        Assert.Equal("Olá Maria, sou Carlos do Comercial. Ticket #4321.", result.Rendered);
        Assert.Empty(result.UnknownVariables);
    }

    [Fact]
    public void UsesFallbacksWhenContextValuesAreMissing()
    {
        var template = "Olá {{client_name}}, sou {{attendant_name}} do {{department_name}}. Ticket #{{ticket_number}}.";
        var ctx = new SubstitutionContext(null, null, null, null);
        var result = VariableSubstitution.Apply(template, ctx);
        Assert.Equal("Olá cliente, sou atendente do atendimento. Ticket #—.", result.Rendered);
        Assert.DoesNotContain("{{", result.Rendered);
    }

    [Fact]
    public void PreservesUnknownVariablesLiterally()
    {
        var template = "Olá {{foo_bar}} e {{client_name}}";
        var result = VariableSubstitution.Apply(template, new SubstitutionContext("Maria", null, null, null));
        Assert.Equal("Olá {{foo_bar}} e Maria", result.Rendered);
        Assert.Contains("foo_bar", result.UnknownVariables);
    }

    [Fact]
    public void EmptyTemplate_ReturnsEmpty()
    {
        var result = VariableSubstitution.Apply(string.Empty, new SubstitutionContext(null, null, null, null));
        Assert.Equal(string.Empty, result.Rendered);
    }

    [Fact]
    public void Performance_LargeTemplate_UnderOneMillisecond()
    {
        var template = string.Concat(Enumerable.Repeat("Olá {{client_name}} ", 200));
        var ctx = new SubstitutionContext("Maria", "Carlos", 1, "Comercial");

        // Warm-up
        for (var i = 0; i < 10; i++) VariableSubstitution.Apply(template, ctx);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) VariableSubstitution.Apply(template, ctx);
        sw.Stop();
        var avgMicros = (sw.Elapsed.TotalMilliseconds * 1000) / 100;
        Assert.True(avgMicros < 1000, $"Average {avgMicros:F1}μs (expected < 1000μs / 1ms)");
    }
}
