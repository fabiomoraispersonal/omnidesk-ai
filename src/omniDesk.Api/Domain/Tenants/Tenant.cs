namespace omniDesk.Api.Domain.Tenants;

public class Tenant
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string RazaoSocial { get; set; } = string.Empty;
    public string? NomeFantasia { get; set; }
    public string Cnpj { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Provisioning;
    public string? OpenAiApiKeyEnc { get; set; }
    public string? OpenAiOrganization { get; set; }
    public string? OpenAiProject { get; set; }
    public string Timezone { get; set; } = "America/Sao_Paulo";
    public string Locale { get; set; } = "pt-BR";
    public string Currency { get; set; } = "BRL";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string? ProvisioningErrorLog { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }

    public ICollection<TenantContact> Contacts { get; set; } = [];

    public string SchemaName => $"tenant_{Slug.Replace('-', '_')}";
    public string BucketName => $"tenant-{Slug}";
    public string MongoDatabaseName => $"tenant_{Slug.Replace('-', '_')}";
    public string RedisPrefix => $"{Slug}:";
}
