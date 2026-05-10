namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Status de entrega de uma mensagem WhatsApp recebido via webhook Meta.
/// Persistido em MongoDB <c>{slug}_wa_message_statuses</c>.
/// </summary>
public enum WaMessageStatus
{
    Sent,
    Delivered,
    Read,
    Failed,
}

public static class WaMessageStatusExtensions
{
    public static string ToWire(this WaMessageStatus value) => value switch
    {
        WaMessageStatus.Sent      => "sent",
        WaMessageStatus.Delivered => "delivered",
        WaMessageStatus.Read      => "read",
        WaMessageStatus.Failed    => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static WaMessageStatus ParseWire(string value) => value switch
    {
        "sent"      => WaMessageStatus.Sent,
        "delivered" => WaMessageStatus.Delivered,
        "read"      => WaMessageStatus.Read,
        "failed"    => WaMessageStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown wa message status."),
    };
}
