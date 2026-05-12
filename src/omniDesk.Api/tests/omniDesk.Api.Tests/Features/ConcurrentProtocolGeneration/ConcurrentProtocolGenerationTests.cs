using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.ConcurrentProtocolGeneration;

/// <summary>
/// Spec 009 T067 — 100 concurrent inserts in the same tenant/day must produce
/// 100 unique protocols (per-day per-tenant sequence: R1 / SC-004).
/// Requires Testcontainers Postgres (Docker).
/// </summary>
[Trait("Category", "Integration")]
public class ConcurrentProtocolGenerationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public ConcurrentProtocolGenerationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task HundredParallelInserts_ProduceHundredUniqueProtocols()
    {
        const int Count = 100;
        const string Slug = "proto-test";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TicketProtocolService>();

        var tasks = Enumerable.Range(0, Count)
            .Select(_ => svc.GenerateAsync(Slug))
            .ToList();

        var protocols = await Task.WhenAll(tasks);

        Assert.Equal(Count, protocols.Distinct().Count());

        // All must follow TK-YYYYMMDD-NNNNN format and match today's date
        foreach (var p in protocols)
        {
            Assert.Matches(@"^TK-\d{8}-\d{5}$", p);
            Assert.Contains(today.ToString("yyyyMMdd"), p);
        }
    }

    [Fact]
    public async Task Protocol_MatchesFormat_TK_YYYYMMDD_Sequence()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TicketProtocolService>();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");

        var p = await svc.GenerateAsync("format-check");

        Assert.StartsWith($"TK-{today}-", p);
        Assert.Equal(15, p.Length); // TK-YYYYMMDD-NNNNN
    }
}
