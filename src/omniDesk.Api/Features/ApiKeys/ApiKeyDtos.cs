namespace omniDesk.Api.Features.ApiKeys;

public record CreateApiKeyRequest(string Name);

public record ApiKeyResponse(
    Guid Id,
    string Name,
    string[] Scopes,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt,
    bool Revoked,
    DateTime CreatedAt);

public record CreatedApiKeyResponse(
    Guid Id,
    string Name,
    string[] Scopes,
    DateTime? LastUsedAt,
    DateTime? ExpiresAt,
    bool Revoked,
    DateTime CreatedAt,
    string Key);
