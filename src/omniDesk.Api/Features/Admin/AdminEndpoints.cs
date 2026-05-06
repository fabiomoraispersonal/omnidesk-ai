using omniDesk.Api.Features.Admin.Impersonate;
using omniDesk.Api.Infrastructure.Auth;

namespace omniDesk.Api.Features.Admin;

public static class AdminEndpointRegistration
{
    public static WebApplication MapAdminEndpoints(this WebApplication app, RouteGroupBuilder admin)
    {
        ImpersonateEndpoint.Map(admin);
        return app;
    }
}
