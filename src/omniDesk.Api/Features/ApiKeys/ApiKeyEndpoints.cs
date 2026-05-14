using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.ApiKeys;

public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/",        ListAsync).RequireAuthorization(policy => policy.RequireRole(Roles.TenantAdmin));
        group.MapPost("/",       CreateAsync).RequireAuthorization(policy => policy.RequireRole(Roles.TenantAdmin));
        group.MapDelete("/{id}", RevokeAsync).RequireAuthorization(policy => policy.RequireRole(Roles.TenantAdmin));
        return group;
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser currentUser,
        ListApiKeysHandler handler,
        CancellationToken ct)
    {
        if (currentUser.TenantId is not Guid tenantId)
            return Results.Unauthorized();

        var keys = await handler.ExecuteAsync(tenantId, ct);
        return Results.Ok(new { success = true, data = keys });
    }

    private static async Task<IResult> CreateAsync(
        CreateApiKeyRequest request,
        ICurrentUser currentUser,
        CreateApiKeyHandler handler,
        CancellationToken ct)
    {
        if (currentUser.TenantId is not Guid tenantId)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { success = false, error = new { code = "VALIDATION_FAILED", message = "Name is required." } });

        var (key, error) = await handler.ExecuteAsync(tenantId, request.Name, ct);

        if (error == "API_KEY_LIMIT_REACHED")
            return Results.UnprocessableEntity(new { success = false, error = new { code = error, message = "Maximum of 5 active API keys reached." } });

        return Results.Created($"/api/api-keys/{key!.Id}", new { success = true, data = key });
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        ICurrentUser currentUser,
        RevokeApiKeyHandler handler,
        CancellationToken ct)
    {
        if (currentUser.TenantId is not Guid tenantId)
            return Results.Unauthorized();

        var revoked = await handler.ExecuteAsync(id, tenantId, ct);
        if (!revoked)
            return Results.NotFound(new { success = false, error = new { code = "API_KEY_NOT_FOUND", message = "API key not found or already revoked." } });

        return Results.Ok(new { success = true });
    }
}
