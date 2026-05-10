using System.Text.Json.Serialization;

namespace omniDesk.Api.Infrastructure.WhatsApp;

/// <summary>
/// Erro retornado pela Meta Graph API. <c>Code</c> é o sub-error Meta
/// (ex: 190 = token expirado; 131047 = fora da janela 24h). Ver MetaApi.ErrorCodes.
/// </summary>
public sealed class MetaApiException : Exception
{
    public int Code { get; }
    public string? FbTraceId { get; }
    public int HttpStatusCode { get; }

    public MetaApiException(int code, string message, string? fbTraceId, int httpStatusCode)
        : base(message)
    {
        Code = code;
        FbTraceId = fbTraceId;
        HttpStatusCode = httpStatusCode;
    }

    public bool IsTokenRevoked  => Code == MetaApi.ErrorCodes.TokenRevoked;
    public bool IsOutsideWindow => Code == MetaApi.ErrorCodes.OutsideWindow;
}

// ---- Send response ----

public sealed record MetaSendResponse(string MessageId);

internal sealed record SendApiResponse(
    [property: JsonPropertyName("messaging_product")] string MessagingProduct,
    [property: JsonPropertyName("messages")]          IReadOnlyList<SendApiResponseMessage> Messages);

internal sealed record SendApiResponseMessage(
    [property: JsonPropertyName("id")] string Id);

// ---- Submit template response ----

public sealed record MetaTemplateSubmissionResponse(string MetaTemplateId, string Status);

internal sealed record SubmitTemplateApiResponse(
    [property: JsonPropertyName("id")]     string Id,
    [property: JsonPropertyName("status")] string Status);

// ---- Get template status ----

public sealed record MetaTemplateStatusInfo(string Name, string Language, string Status, string? Id);

internal sealed record GetTemplateStatusApiResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<TemplateStatusItem> Data);

internal sealed record TemplateStatusItem(
    [property: JsonPropertyName("name")]     string Name,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("status")]   string Status,
    [property: JsonPropertyName("id")]       string? Id);

// ---- Media metadata ----

public sealed record MetaMediaInfo(
    string Url,
    string MimeType,
    string Sha256,
    long FileSize,
    string Id);

internal sealed record MediaInfoApiResponse(
    [property: JsonPropertyName("url")]               string Url,
    [property: JsonPropertyName("mime_type")]         string MimeType,
    [property: JsonPropertyName("sha256")]            string Sha256,
    [property: JsonPropertyName("file_size")]         long FileSize,
    [property: JsonPropertyName("id")]                string Id,
    [property: JsonPropertyName("messaging_product")] string MessagingProduct);

// ---- Error response ----

internal sealed record MetaErrorEnvelope(
    [property: JsonPropertyName("error")] MetaError Error);

internal sealed record MetaError(
    [property: JsonPropertyName("message")]    string Message,
    [property: JsonPropertyName("type")]       string? Type,
    [property: JsonPropertyName("code")]       int Code,
    [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
    [property: JsonPropertyName("fbtrace_id")] string? FbTraceId);

// ---- Template submission payload (request) ----

public sealed record TemplateSubmissionPayload(
    string Name,
    string Category,
    string Language,
    IReadOnlyList<TemplateComponent> Components);

public sealed record TemplateComponent(
    string Type,
    string? Text,
    TemplateComponentExample? Example);

public sealed record TemplateComponentExample(IReadOnlyList<IReadOnlyList<string>> BodyText);

// ---- Send template payload (request) ----

public sealed record TemplateSendPayload(
    string TemplateName,
    string Language,
    IReadOnlyList<TemplateSendParameter> Parameters);

public sealed record TemplateSendParameter(string Type, string Text);
