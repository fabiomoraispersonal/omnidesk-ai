using omniDesk.Api.Features.Admin.AgentTemplates;
using omniDesk.Api.Features.Admin.Tenants;

namespace omniDesk.Api.Features.Admin;

public static class AdminEndpointRegistration
{
    public static WebApplication MapAdminEndpoints(this WebApplication app, RouteGroupBuilder admin)
    {
        TenantsEndpoints.Map(admin);
        AgentTemplatesEndpoints.Map(admin);
        return app;
    }
}
