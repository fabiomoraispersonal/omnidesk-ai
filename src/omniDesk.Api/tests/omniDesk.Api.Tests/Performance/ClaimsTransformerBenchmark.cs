using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Authz;
using Xunit;

namespace omniDesk.Api.Tests.Performance;

/// <summary>
/// Spec 004 / T065 — verifica que a avaliação de policy custa ≤ 1 ms p95
/// (Performance Goal do plan.md).
/// </summary>
public class ClaimsTransformerBenchmark
{
    [Fact]
    public async Task PolicyEvaluation_p95_Below1ms()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
        services.AddTransient<IAuthorizationHandler, DepartmentScopeHandler>();
        AuthorizationPoliciesRegistration.Register(services);
        var provider = services.BuildServiceProvider();
        var authz = provider.GetRequiredService<IAuthorizationService>();

        var id = new ClaimsIdentity("Test");
        id.AddClaim(new Claim("role", Roles.Supervisor));
        var principal = new ClaimsPrincipal(id);

        // Warm-up
        for (var i = 0; i < 100; i++)
            await authz.AuthorizeAsync(principal, null, Policies.CanCreateAttendant);

        const int Iterations = 2000;
        var ticks = new long[Iterations];
        var sw = new Stopwatch();
        for (var i = 0; i < Iterations; i++)
        {
            sw.Restart();
            await authz.AuthorizeAsync(principal, null, Policies.CanCreateAttendant);
            sw.Stop();
            ticks[i] = sw.ElapsedTicks;
        }

        Array.Sort(ticks);
        var p95 = ticks[(int)(Iterations * 0.95)];
        var p95Ms = p95 * 1000.0 / Stopwatch.Frequency;

        Assert.True(p95Ms < 1.0, $"p95 = {p95Ms:F3}ms (must be < 1ms)");
    }
}
