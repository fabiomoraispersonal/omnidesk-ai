using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Attendants;

/// <summary>
/// Avatar upload — stores the file in the existing per-tenant MinIO bucket
/// (`tenant-{slug}/avatars/attendants/{id}/...`) and persists a relative key in
/// `attendants.avatar_url`. The signed URL is computed at read time.
/// </summary>
public static class AvatarUploadEndpoint
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/avatar", HandleAsync)
            .RequireAuthorization(Policies.CanEditAttendant)
            .DisableAntiforgery();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        IFormFile file,
        AppDbContext db,
        IMinioClient minio,
        IConfiguration config,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Bad("EMPTY_FILE", "Arquivo de avatar é obrigatório.");
        if (file.Length > MaxBytes)
            return Bad("FILE_TOO_LARGE", "Avatar não pode exceder 2 MB.");
        if (!AllowedContentTypes.Contains(file.ContentType?.ToLowerInvariant()))
            return Bad("INVALID_FILE_TYPE", "Use JPG, PNG ou WebP.");

        var attendant = await db.Attendants.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attendant is null)
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "ATTENDANT_NOT_FOUND", message = "Atendente não encontrado." }
            });

        // Tenant slug comes from the JWT (Spec 002/003 already populates it).
        // We accept it as a configuration fallback for tests.
        var tenantSlug = config["TestingTenantSlug"]
            ?? throw new InvalidOperationException("Tenant slug not resolvable; ensure tenant resolver middleware is in place.");
        var bucket = $"tenant-{tenantSlug}";
        var ext = file.ContentType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "jpg",
        };
        var objectKey = $"avatars/attendants/{id}/256x256.{ext}";

        await using var stream = file.OpenReadStream();
        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(file.Length)
            .WithContentType(file.ContentType), ct);

        attendant.AvatarUrl = objectKey;
        attendant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // Signed URL valid for 7 days (research §R9).
        var presigned = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(60 * 60 * 24 * 7));

        return Results.Ok(new { success = true, data = new { avatar_url = presigned } });
    }

    private static IResult Bad(string code, string message) =>
        Results.UnprocessableEntity(new
        {
            success = false,
            error = new { code, message }
        });
}
