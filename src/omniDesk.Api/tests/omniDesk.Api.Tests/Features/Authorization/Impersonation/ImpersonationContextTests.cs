using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Features.Authorization.Impersonation;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class ImpersonationContextTests
{
    private static HttpContext ContextWith(params (string type, string value)[] claims)
    {
        var id = new ClaimsIdentity("Test");
        foreach (var (t, v) in claims) id.AddClaim(new Claim(t, v));
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(id) };
        return ctx;
    }

    private sealed class StubAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private static LogEvent NewEvent() => new(
        DateTimeOffset.Now, LogEventLevel.Information, exception: null,
        new MessageTemplate("test", Array.Empty<MessageTemplateToken>()),
        Array.Empty<LogEventProperty>());

    [Fact]
    public void Enricher_AddsImpersonationProperties_WhenClaimPresent()
    {
        var accessor = new StubAccessor
        {
            HttpContext = ContextWith(
                ("impersonating", "true"),
                ("impersonated_by", "saas_admin"),
                ("jti", "abc-123"),
                ("tenant_slug", "clinica-x")),
        };
        var enricher = new ImpersonationAuditEnricher(accessor);
        var evt = NewEvent();
        enricher.Enrich(evt, new SimplePropertyFactory());

        Assert.True(evt.Properties.ContainsKey("Impersonating"));
        Assert.Equal("\"saas_admin\"", evt.Properties["ImpersonatedBy"].ToString());
        Assert.Equal("\"abc-123\"", evt.Properties["Jti"].ToString());
        Assert.Equal("\"clinica-x\"", evt.Properties["TenantSlug"].ToString());
    }

    [Fact]
    public void Enricher_DoesNothing_WhenNotImpersonating()
    {
        var accessor = new StubAccessor
        {
            HttpContext = ContextWith(("role", "tenant_admin")),
        };
        var enricher = new ImpersonationAuditEnricher(accessor);
        var evt = NewEvent();
        enricher.Enrich(evt, new SimplePropertyFactory());
        Assert.False(evt.Properties.ContainsKey("Impersonating"));
    }

    private sealed class SimplePropertyFactory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}
