using System.Text.Json.Serialization;

namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// DTOs para deserialização do payload de webhook Meta. Apenas os campos consumidos
/// pelo adapter — Meta envia muitos extras que ignoramos. Use snake_case naming
/// via <c>JsonNamingPolicy.SnakeCaseLower</c> nos options padrão.
/// </summary>
public sealed record WaWebhookPayload(
    [property: JsonPropertyName("object")] string? Object,
    [property: JsonPropertyName("entry")]  IReadOnlyList<WaWebhookEntry>? Entry);

public sealed record WaWebhookEntry(
    [property: JsonPropertyName("id")]      string? Id,
    [property: JsonPropertyName("changes")] IReadOnlyList<WaMessagesChange>? Changes);

public sealed record WaMessagesChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("value")] WaMessagesValue Value);

public sealed record WaMessagesValue(
    [property: JsonPropertyName("messaging_product")] string? MessagingProduct,
    [property: JsonPropertyName("metadata")]          WaWebhookMetadata? Metadata,
    [property: JsonPropertyName("contacts")]          IReadOnlyList<WaContact>? Contacts,
    [property: JsonPropertyName("messages")]          IReadOnlyList<WaIncomingMessage>? Messages,
    [property: JsonPropertyName("statuses")]          IReadOnlyList<WaStatusUpdate>? Statuses,

    // Template status updates (when field == message_template_status_update)
    [property: JsonPropertyName("event")]                     string? Event,
    [property: JsonPropertyName("message_template_id")]       long? MessageTemplateId,
    [property: JsonPropertyName("message_template_name")]     string? MessageTemplateName,
    [property: JsonPropertyName("message_template_language")] string? MessageTemplateLanguage,
    [property: JsonPropertyName("reason")]                    string? Reason);

public sealed record WaWebhookMetadata(
    [property: JsonPropertyName("display_phone_number")] string? DisplayPhoneNumber,
    [property: JsonPropertyName("phone_number_id")]      string? PhoneNumberId);

public sealed record WaContact(
    [property: JsonPropertyName("profile")] WaContactProfile? Profile,
    [property: JsonPropertyName("wa_id")]   string? WaId);

public sealed record WaContactProfile(
    [property: JsonPropertyName("name")] string? Name);

public sealed record WaIncomingMessage(
    [property: JsonPropertyName("from")]      string From,
    [property: JsonPropertyName("id")]        string Id,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("type")]      string Type,

    [property: JsonPropertyName("text")]     WaTextContent? Text,
    [property: JsonPropertyName("image")]    WaMediaContent? Image,
    [property: JsonPropertyName("document")] WaMediaContent? Document,
    [property: JsonPropertyName("audio")]    WaMediaContent? Audio,
    [property: JsonPropertyName("video")]    WaMediaContent? Video,
    [property: JsonPropertyName("sticker")]  WaMediaContent? Sticker);

public sealed record WaTextContent(
    [property: JsonPropertyName("body")] string? Body);

public sealed record WaMediaContent(
    [property: JsonPropertyName("id")]        string? Id,
    [property: JsonPropertyName("mime_type")] string? MimeType,
    [property: JsonPropertyName("sha256")]    string? Sha256,
    [property: JsonPropertyName("caption")]   string? Caption,
    [property: JsonPropertyName("filename")]  string? Filename,
    [property: JsonPropertyName("voice")]     bool? Voice);

public sealed record WaStatusUpdate(
    [property: JsonPropertyName("id")]           string Id,
    [property: JsonPropertyName("status")]       string Status,
    [property: JsonPropertyName("timestamp")]    string? Timestamp,
    [property: JsonPropertyName("recipient_id")] string? RecipientId,
    [property: JsonPropertyName("errors")]       IReadOnlyList<WaStatusError>? Errors);

public sealed record WaStatusError(
    [property: JsonPropertyName("code")]    int Code,
    [property: JsonPropertyName("title")]   string? Title,
    [property: JsonPropertyName("message")] string? Message);
