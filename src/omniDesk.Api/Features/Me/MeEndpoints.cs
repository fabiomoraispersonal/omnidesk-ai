namespace omniDesk.Api.Features.Me;

public static class MeEndpointRegistration
{
    public static WebApplication MapMeEndpoints(this WebApplication app, RouteGroupBuilder me)
    {
        GetProfileEndpoint.Map(me);
        UpdateProfileEndpoint.Map(me);
        ChangePasswordEndpoint.Map(me);
        GetPermissionsEndpoint.Map(me);
        return app;
    }
}
