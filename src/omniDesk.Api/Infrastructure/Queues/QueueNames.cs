namespace omniDesk.Api.Infrastructure.Queues;

public static class QueueNames
{
    public static string Incoming(string slug) => $"{slug}-incoming-messages";
    public static string Outgoing(string slug) => $"{slug}-outgoing-messages";

    // Hangfire queue names: lowercase, no underscores recommended.
    public const string IncomingFamily = "ai-incoming";
    public const string OutgoingFamily = "ai-outgoing";
}
