using Minio;
using Minio.DataModel.Args;

namespace omniDesk.Api.Infrastructure.Provisioning;

public class MinioProvisioner(IMinioClient minio, ILogger<MinioProvisioner> logger)
{
    public async Task CreateBucketAsync(string slug, CancellationToken ct = default)
    {
        var bucketName = $"tenant-{slug}";

        var exists = await minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName), ct);

        if (exists)
        {
            logger.LogInformation("MinIO bucket {Bucket} already exists, skipping.", bucketName);
            return;
        }

        await minio.MakeBucketAsync(
            new MakeBucketArgs().WithBucket(bucketName), ct);

        logger.LogInformation("MinIO bucket {Bucket} created.", bucketName);
    }
}
