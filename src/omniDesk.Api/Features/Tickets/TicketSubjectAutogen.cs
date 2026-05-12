namespace omniDesk.Api.Features.Tickets;

// Generates a ticket subject from conversation context (FR-040).
public static class TicketSubjectAutogen
{
    private const int MaxLength = 100;

    /// <summary>
    /// Generates a subject from the last non-null message text.
    /// Falls back to "Atendimento via {canal}" for media-only conversations.
    /// </summary>
    public static string Generate(string? lastMessageText, string channelLabel)
    {
        if (string.IsNullOrWhiteSpace(lastMessageText))
            return $"Atendimento via {channelLabel}";

        var trimmed = lastMessageText.Trim();
        return trimmed.Length <= MaxLength
            ? trimmed
            : trimmed[..MaxLength];
    }
}
