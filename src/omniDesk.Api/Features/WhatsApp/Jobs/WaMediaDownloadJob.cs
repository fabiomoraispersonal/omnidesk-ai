using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.LiveChat.Uploads;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.WhatsApp;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Jobs;

/// <summary>
/// Spec 008 US6 T129 — baixa mídia da Meta e armazena em MinIO tenant-scoped.
/// Pipeline:
/// <list type="number">
///   <item>Meta <c>GET /{media_id}</c> → URL temporária + mime + size.</item>
///   <item>Valida size ≤ 100 MB (limite Meta).</item>
///   <item>Meta <c>GET {url}</c> com Bearer access_token → bytes.</item>
///   <item><see cref="MimeTypeDetector"/> magic-byte detection — rejeita não-allowlist.</item>
///   <item>Upload MinIO <c>tenant-{slug}/whatsapp-attachments/{conv_id}/{wa_msg_id}-{filename}</c>.</item>
///   <item>Update <c>messages.attachment_url</c> + WS broadcast <c>wa.message_status</c>
///     com <c>attachment_ready=true</c>.</item>
/// </list>
/// Em falha: marca <c>attachment_url</c> com placeholder de erro (sem retry; mídia Meta
/// expira em ~5min e retry seria inútil).
/// </summary>
public sealed class WaMediaDownloadJob
{
    private const long MaxBytes = 100L * 1024 * 1024; // 100 MB — limite Meta
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly WhatsAppMetaClient _meta;
    private readonly IWhatsAppConfigRepository _configRepo;
    private readonly AesEncryptionService _aes;
    private readonly MimeTypeDetector _mimeDetector;
    private readonly IMinioClient _minio;
    private readonly IConnectionMultiplexer _redis;
    private readonly TenantContextHolder _tenantContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<WaMediaDownloadJob> _logger;

    public WaMediaDownloadJob(
        AppDbContext db,
        WhatsAppMetaClient meta,
        IWhatsAppConfigRepository configRepo,
        AesEncryptionService aes,
        MimeTypeDetector mimeDetector,
        IMinioClient minio,
        IConnectionMultiplexer redis,
        TenantContextHolder tenantContext,
        TimeProvider clock,
        ILogger<WaMediaDownloadJob> logger)
    {
        _db = db;
        _meta = meta;
        _configRepo = configRepo;
        _aes = aes;
        _mimeDetector = mimeDetector;
        _minio = minio;
        _redis = redis;
        _tenantContext = tenantContext;
        _clock = clock;
        _logger = logger;
    }

    [Queue("wa-media-download")]
    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(
        string tenantSlug,
        Guid tenantId,
        Guid messageId,
        string mediaId,
        string? originalFileName,
        string mediaType,
        CancellationToken ct)
    {
        _tenantContext.Set(tenantSlug, tenantId);

        var message = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null)
        {
            _logger.LogWarning("WaMediaDownload: message {MessageId} not found.", messageId);
            return;
        }

        var config = await _configRepo.GetByTenantIdAsync(tenantId, ct);
        if (config is null || !config.HasAccessToken)
        {
            _logger.LogWarning("WaMediaDownload: missing config/token for tenant {Slug}.", tenantSlug);
            await MarkFailedAsync(message, "no_config", ct);
            return;
        }

        string accessToken;
        try
        {
            accessToken = _aes.Decrypt(config.AccessTokenCiphertext!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WaMediaDownload: failed to decrypt access_token for tenant {Slug}.", tenantSlug);
            await MarkFailedAsync(message, "token_decrypt_failed", ct);
            return;
        }

        // Step 1: Meta GET /{media_id} → URL temporária.
        MetaMediaInfo info;
        try
        {
            info = await _meta.GetMediaInfoAsync(mediaId, accessToken, ct);
        }
        catch (MetaApiException ex)
        {
            _logger.LogWarning(
                "WaMediaDownload: Meta GetMediaInfo failed. tenant={Slug} mediaId={MediaId} code={Code}",
                tenantSlug, mediaId, ex.Code);
            await MarkFailedAsync(message, "meta_info_failed", ct);
            return;
        }

        if (info.FileSize > MaxBytes)
        {
            _logger.LogWarning(
                "WaMediaDownload: media exceeds size limit. tenant={Slug} size={Size}",
                tenantSlug, info.FileSize);
            await MarkFailedAsync(message, "exceeds_size_limit", ct);
            return;
        }

        // Step 2: Meta GET URL temporária → bytes.
        byte[] bytes;
        try
        {
            bytes = await _meta.DownloadMediaBytesAsync(info.Url, accessToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WaMediaDownload: bytes fetch failed. tenant={Slug} mediaId={MediaId}",
                tenantSlug, mediaId);
            await MarkFailedAsync(message, "download_failed", ct);
            return;
        }

        // Step 3: validar magic bytes — proteção contra disguised content (Constituição §IV).
        string? realMime;
        using (var ms = new MemoryStream(bytes, writable: false))
        {
            realMime = await _mimeDetector.DetectAsync(ms, ct);
        }
        if (realMime is null || !MimeTypeDetector.Allowlist.Contains(realMime))
        {
            _logger.LogWarning(
                "WaMediaDownload: real MIME rejected. tenant={Slug} declared={Declared} detected={Detected}",
                tenantSlug, info.MimeType, realMime ?? "(unknown)");
            await MarkFailedAsync(message, "unsupported_media_type", ct);
            return;
        }

        // Step 4: upload MinIO tenant-scoped.
        var bucket = $"tenant-{tenantSlug}";
        var safeFileName = SanitizeFileName(originalFileName ?? DeriveFileNameFromMime(realMime));
        var objectKey = $"whatsapp-attachments/{message.ConversationId:D}/{message.WaMessageId}-{safeFileName}";

        try
        {
            using var uploadStream = new MemoryStream(bytes, writable: false);
            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithStreamData(uploadStream)
                .WithObjectSize(bytes.LongLength)
                .WithContentType(realMime), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WaMediaDownload: MinIO upload failed. tenant={Slug} bucket={Bucket} key={Key}",
                tenantSlug, bucket, objectKey);
            await MarkFailedAsync(message, "minio_upload_failed", ct);
            return;
        }

        // Step 5: presigned URL (7 dias TTL) para o CRM exibir.
        string presigned;
        try
        {
            presigned = await _minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithExpiry(60 * 60 * 24 * 7));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "WaMediaDownload: presign failed; saving object key as fallback. tenant={Slug}", tenantSlug);
            presigned = $"minio://{bucket}/{objectKey}";
        }

        message.AttachmentUrl = presigned;
        message.AttachmentName = safeFileName;
        message.AttachmentSizeBytes = (int)Math.Min(int.MaxValue, bytes.LongLength);
        await _db.SaveChangesAsync(ct);

        // Step 6: WS broadcast attachment_ready.
        await BroadcastAttachmentReadyAsync(tenantSlug, message, ct);

        _logger.LogInformation(
            "WaMediaDownload: success. tenant={Slug} mediaId={MediaId} size={Size} mime={Mime} key={Key}",
            tenantSlug, mediaId, bytes.LongLength, realMime, objectKey);
    }

    private async Task MarkFailedAsync(omniDesk.Api.Domain.LiveChat.Message message, string reason, CancellationToken ct)
    {
        // Não temos JSONB metadata em messages; usamos AttachmentName como sinal de falha
        // (UI exibe "Falha ao carregar mídia"). Caller verifica AttachmentUrl null + name "_failed:*".
        message.AttachmentName = $"_failed:{reason}";
        await _db.SaveChangesAsync(ct);
    }

    private async Task BroadcastAttachmentReadyAsync(string slug, omniDesk.Api.Domain.LiveChat.Message message, CancellationToken ct)
    {
        var conv = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == message.ConversationId)
            .Select(c => new { c.AttendantId, c.DepartmentId })
            .FirstOrDefaultAsync(ct);

        if (conv is null) return;

        var payload = JsonSerializer.Serialize(new
        {
            type = WhatsAppCrmEvents.WaMessageStatus,
            payload = new
            {
                conversation_id = message.ConversationId,
                message_id = message.Id,
                wa_message_id = message.WaMessageId,
                status = "attachment_ready",
                timestamp = _clock.GetUtcNow(),
                attachment_ready = true,
                attachment_url = message.AttachmentUrl,
            },
        }, JsonOpts);

        var sub = _redis.GetSubscriber();
        if (conv.AttendantId is { } att)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmUser(slug, att)),
                payload);
        }
        if (conv.DepartmentId is { } dept)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmDepartment(slug, dept)),
                payload);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (clean.Length > 100) clean = clean[..100];
        return string.IsNullOrWhiteSpace(clean) ? "file" : clean;
    }

    private static string DeriveFileNameFromMime(string mime) => mime switch
    {
        MimeTypeDetector.Jpeg     => "image.jpg",
        MimeTypeDetector.Png      => "image.png",
        MimeTypeDetector.Gif      => "image.gif",
        MimeTypeDetector.Webp     => "image.webp",
        MimeTypeDetector.Pdf      => "document.pdf",
        MimeTypeDetector.Docx     => "document.docx",
        MimeTypeDetector.Xlsx     => "document.xlsx",
        MimeTypeDetector.OggAudio => "audio.ogg",
        MimeTypeDetector.Mp3      => "audio.mp3",
        MimeTypeDetector.Aac      => "audio.aac",
        MimeTypeDetector.Mp4Audio => "audio.m4a",
        _ => "media",
    };
}
