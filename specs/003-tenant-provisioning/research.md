# Research: Tenants (Provisionamento)

**Branch**: `003-tenant-provisioning` | **Data**: 2026-05-06

---

## 1. EF Core — Migrations Programáticas para Schema Dinâmico (PostgreSQL)

**Decisão**: Criar uma instância de `DbContext` especializada (`TenantDbContext`) configurada com o schema do tenant em tempo de execução. O `TenantDbContext` recebe o schema name no construtor e o aplica via `HasDefaultSchema()` no `OnModelCreating`. A migration é executada via `await context.Database.MigrateAsync()`.

**Implementação**:
```csharp
// Infrastructure/Provisioning/TenantSchemaProvisioner.cs
public async Task ProvisionSchemaAsync(string slug, CancellationToken ct)
{
    var schemaName = $"tenant_{slug.Replace('-', '_')}";

    // 1. Criar o schema manualmente (segurança extra antes do migrate)
    await using var adminContext = _adminDbContextFactory.CreateDbContext();
    await adminContext.Database.ExecuteSqlRawAsync(
        $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\"", ct);

    // 2. Criar DbContext para o schema do tenant
    var options = new DbContextOptionsBuilder<TenantDbContext>()
        .UseNpgsql(_connectionString, o =>
            o.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
        .Options;

    await using var tenantContext = new TenantDbContext(options, schemaName);
    await tenantContext.Database.MigrateAsync(ct);
}
```

```csharp
// Infrastructure/Persistence/TenantDbContext.cs
public class TenantDbContext(DbContextOptions<TenantDbContext> options, string schema)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);
        // entidades do tenant: agents, contacts, tickets, etc.
    }
}
```

**Tabela de histórico de migration por schema**: A opção `MigrationsHistoryTable("__EFMigrationsHistory", schemaName)` garante que cada tenant tenha sua própria tabela de histórico, isolando completamente as migrations.

**Rationale**: Abordagem padrão para multi-tenant schema-per-tenant com EF Core + Npgsql. Alternativas como raw SQL para aplicar migrations foram rejeitadas — perdem a rastreabilidade do histórico de migrations e o type-safety do EF Core.

**Alternativas consideradas**:
- SQL raw para DDL: rejeitado — sem rastreamento de versão de migration por schema.
- `IDesignTimeDbContextFactory` compartilhado: inadequado — o factory é para design-time, não runtime dinâmico.

---

## 2. MinIO .NET SDK — Criação de Bucket

**Decisão**: Pacote `Minio` (NuGet oficial). Instância singleton de `MinioClient` injetada via DI. No provisionamento, criar o bucket com verificação prévia de existência.

**Implementação**:
```csharp
// Infrastructure/Provisioning/MinioProvisioner.cs
public async Task CreateBucketAsync(string slug, CancellationToken ct)
{
    var bucketName = $"tenant-{slug}";

    var exists = await _minioClient.BucketExistsAsync(
        new BucketExistsArgs().WithBucket(bucketName), ct);

    if (!exists)
    {
        await _minioClient.MakeBucketAsync(
            new MakeBucketArgs().WithBucket(bucketName), ct);
    }
}
```

**ARM64**: O pacote `Minio` é pure .NET — sem dependências nativas, totalmente compatível com `linux/arm64`.

**Configuração DI**:
```csharp
// Program.cs
builder.Services.AddSingleton<IMinioClient>(sp =>
    new MinioClient()
        .WithEndpoint(builder.Configuration["MinIO:Endpoint"])
        .WithCredentials(
            builder.Configuration["MinIO:AccessKey"],
            builder.Configuration["MinIO:SecretKey"])
        .Build());
```

**Rationale**: `BucketExistsAsync` antes do `MakeBucketAsync` garante idempotência no retry de provisionamento — bucket já criado não causa erro.

**Alternativas consideradas**:
- AWS S3 SDK com endpoint MinIO: mais verboso e requer configuração adicional de região fake.

---

## 3. MongoDB .NET Driver — Inicialização de Database por Tenant

**Decisão**: MongoDB cria databases implicitamente na primeira operação. Na inicialização do tenant, inserir e imediatamente excluir um documento sentinel para forçar a criação explícita do database — garantindo que o database existe e é verificável.

**Implementação**:
```csharp
// Infrastructure/Provisioning/MongoProvisioner.cs
public async Task InitializeDatabaseAsync(string slug, CancellationToken ct)
{
    var dbName = $"tenant_{slug.Replace('-', '_')}";
    var db = _mongoClient.GetDatabase(dbName);

    // Força criação explícita do database via coleção de metadados
    var metadata = db.GetCollection<BsonDocument>("__metadata");
    await metadata.InsertOneAsync(new BsonDocument
    {
        ["tenant_slug"] = slug,
        ["provisioned_at"] = DateTime.UtcNow
    }, cancellationToken: ct);
}
```

**Rationale**: Inserção de documento de metadados cria o database de forma explícita e verificável. O documento de metadados pode ser útil para diagnóstico futuro. Não excluir o documento — é informação útil de auditoria.

**Alternativas consideradas**:
- Criar apenas o handle do database sem inserção: rejeitado — o database não existe de fato até a primeira operação, dificultando verificação de saúde.
- `CreateCollectionAsync()`: válido, mas inserção de metadados é mais informativa.

---

## 4. Hangfire — Job de Provisionamento On-Demand + Job Recorrente de Métricas

**Decisão A — Provisionamento (fire-and-forget)**: Enfileirar um job Hangfire imediatamente após a criação do registro do tenant. O job executa de forma assíncrona e atualiza o status do tenant ao final.

```csharp
// Application/Admin/Tenants/CreateTenantCommand.cs
// Após salvar o tenant com status Provisioning:
var jobId = _backgroundJobClient.Enqueue<ITenantProvisioningJob>(
    j => j.ProvisionAsync(tenantId, CancellationToken.None));
```

**Decisão B — Métricas (recorrente)**: Job Hangfire com cron a cada 5 minutos. Registrado na inicialização da aplicação.

```csharp
// Program.cs
app.UseHangfireDashboard(); // opcional — admin interno
RecurringJob.AddOrUpdate<ITenantMetricsCollectorJob>(
    "collect-tenant-metrics",
    j => j.CollectAllAsync(CancellationToken.None),
    "*/5 * * * *");
```

**Tratamento de erro no provisionamento**: O job usa try/catch. Em falha, atualiza `tenant.Status = TenantStatus.Error` e persiste o log de erro em campo `provisioning_error_log` (ver data-model).

**Idempotência no retry**: Antes de cada etapa do provisionamento, verificar se o recurso já existe (schema, bucket, database, usuário). Se existir, pular para a próxima etapa.

**Rationale**: Hangfire já está no stack da constituição. Fire-and-forget é o padrão correto para tarefas de longa duração iniciadas por uma requisição HTTP.

**Alternativas consideradas**:
- RabbitMQ: explicitamente adiado para V2+ por ADR-004.
- Task.Run: rejeitado — sem persistência, retry automático ou visibilidade no dashboard.

---

## 5. Validação de CNPJ com FluentValidation

**Decisão**: Implementar `CnpjValidator` customizado como `PropertyValidator<string>` do FluentValidation. Validação em dois passos: (1) formato e caracteres; (2) dígitos verificadores.

**Algoritmo dos dígitos verificadores**:
```csharp
public static bool IsValidCnpj(string cnpj)
{
    cnpj = Regex.Replace(cnpj, @"[^\d]", "");
    if (cnpj.Length != 14) return false;
    if (cnpj.Distinct().Count() == 1) return false; // "00000000000000" é inválido

    int[] multipliers1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    int[] multipliers2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    var digit1 = CalculateDigit(cnpj[..12], multipliers1);
    var digit2 = CalculateDigit(cnpj[..13], multipliers2);

    return cnpj[12] - '0' == digit1 && cnpj[13] - '0' == digit2;
}

private static int CalculateDigit(string cnpj, int[] multipliers)
{
    var sum = cnpj.Select((c, i) => (c - '0') * multipliers[i]).Sum();
    var remainder = sum % 11;
    return remainder < 2 ? 0 : 11 - remainder;
}
```

**Uso no FluentValidation**:
```csharp
RuleFor(x => x.Cnpj)
    .NotEmpty()
    .Must(CnpjValidator.IsValidCnpj)
    .WithMessage("CNPJ inválido — verifique o formato e os dígitos verificadores.");
```

**Rationale**: Implementação própria evita dependência de pacotes de terceiros para validação simples e estável. O algoritmo é público e não muda.

**Alternativas consideradas**:
- Pacote `Brasil.Gov.CNPJ`: rejeitado — dependência extra para uma única função simples.

---

## 6. Redis — Invalidação em Massa de Sessões por Padrão de Chave

**Decisão**: Usar `IServer.Keys(pattern)` do StackExchange.Redis para buscar todas as chaves do padrão `{slug}:session:*` e deletá-las em batch.

**Implementação**:
```csharp
// Infrastructure/Security/SessionInvalidationService.cs
public async Task InvalidateAllTenantSessionsAsync(string slug)
{
    var pattern = $"{slug}:session:*";
    var endpoints = _connectionMultiplexer.GetEndPoints();
    var server = _connectionMultiplexer.GetServer(endpoints.First());

    var keys = server.Keys(pattern: pattern).ToArray();
    if (keys.Length > 0)
    {
        await _connectionMultiplexer.GetDatabase().KeyDeleteAsync(keys);
    }
}
```

**Performance**: `SCAN`-based (não `KEYS`) — StackExchange.Redis usa `SCAN` internamente via `IServer.Keys()`. Em V1 com dezenas de tenants e poucas sessões por tenant, a performance é irrelevante. Para V2+ com milhares de sessões, avaliar Redis Cluster e prefixo de hash tag.

**Rationale**: Abordagem direta com primitivos do Redis. Alternativas como Lua scripts adicionam complexidade sem ganho em V1.

**Alternativas consideradas**:
- Lua script para atomicidade: rejeitado — a deleção de sessões não é crítica de ser atômica (uma sessão perdida entre SCAN e DEL não é problemática).

---

## 7. AES-256-GCM — Criptografia da API Key da OpenAI

**Decisão**: Mesma abordagem estabelecida na Spec 002 para TOTP secret. AES-256-GCM com nonce único de 12 bytes por operação de criptografia. Chave derivada de variável de ambiente `AES_ENCRYPTION_KEY` (32 bytes em Base64). Formato de armazenamento: `<nonce_hex>:<ciphertext_hex>`.

**Reutilização**: O serviço `AesEncryptionService` já existe (ou será criado) pela Spec 002. Esta feature reutiliza o mesmo serviço — nenhuma nova implementação de criptografia necessária.

**Indicador de presença**: A API retorna `{ "has_openai_key": true/false }` — nunca o valor da chave. O serviço de descriptografia só é invocado internamente ao usar a API da OpenAI.

**Rationale**: Reutilizar implementação existente mantém consistência e reduz superfície de ataque.

---

## 8. Impersonation — Duração 15 min vs 5 min (Resolução de Conflito)

**Decisão**: A duração do token de impersonation para o módulo de Tenants é **15 minutos**, conforme especificado na Spec 003. A Spec 002 havia estabelecido 5 minutos para o mesmo fluxo, mas no contexto de User Story 6 daquela spec (acesso de suporte). Esta spec prevalece para o endpoint `/api/admin/tenants/{id}/impersonate`.

**Impacto na implementação**: O gerador de tokens de impersonation deve aceitar a duração como parâmetro configurável. O endpoint de impersonation de tenants passa `TimeSpan.FromMinutes(15)`.

**Constituição**: O limite constitucional é `JWT access tokens MUST expire in ≤ 15 minutes`. 15 min está dentro do limite — sem violação.

**Alternativas consideradas**:
- Manter 5 min da Spec 002: rejeitado — o documento de requisitos original do módulo de Tenants especifica explicitamente 15 min; 5 min pode ser insuficiente para diagnóstico de problemas complexos.
