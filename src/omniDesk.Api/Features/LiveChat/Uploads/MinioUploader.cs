using Minio;
using Minio.DataModel.Args;

namespace omniDesk.Api.Features.LiveChat.Uploads;

/// <summary>
/// Spec 007 — uploads visitor attachments into the tenant's MinIO bucket under
/// <c>widget-uploads/{conversation_id}/{uuid}-{file}</c> and returns the object key
/// (callers presign at read time).
/// </summary>
public class MinioUploader
{
    private readonly IMinioClient _minio;
    private readonly ILogger<MinioUploader> _logger;

    public MinioUploader(IMinioClient minio, ILogger<MinioUploader> logger)
    {
        _minio = minio;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(
        string slug,
        Guid conversationId,
        string fileName,
        string mime,
        Stream content,
        long size,
        CancellationToken ct)
    {
        var safeName = SanitizeFileName(fileName);
        var bucket = $"tenant-{slug}";
        var objectKey = $"widget-uploads/{conversationId:D}/{Guid.NewGuid():N}-{safeName}";

        await _minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(content)
            .WithObjectSize(size)
            .WithContentType(mime), ct);

        var presigned = await _minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(60 * 60 * 24 * 7));

        _logger.LogInformation(
            "Widget upload stored at {Bucket}/{ObjectKey} for conversation {ConvId}",
            bucket, objectKey, conversationId);

        return new UploadResult(objectKey, presigned, safeName, mime, size);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (clean.Length > 100) clean = clean[..100];
        return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
    }
}

public record UploadResult(string ObjectKey, string PresignedUrl, string FileName, string Mime, long Size);
