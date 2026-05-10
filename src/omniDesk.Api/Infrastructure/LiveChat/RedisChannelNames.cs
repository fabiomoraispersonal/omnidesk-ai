namespace omniDesk.Api.Infrastructure.LiveChat;

/// <summary>
/// Redis Pub/Sub and key naming for the Live Chat module. All names tenant-prefixed
/// (Constitution §I — Multi-Tenant Isolation).
/// </summary>
public static class RedisChannelNames
{
    public static string Conversation(string slug, Guid conversationId)
        => $"{slug}:conv:{conversationId:D}";

    public static string CrmUser(string slug, Guid userId)
        => $"{slug}:crm:user:{userId:D}";

    public static string CrmDepartment(string slug, Guid departmentId)
        => $"{slug}:crm:dept:{departmentId:D}";

    public static string WidgetRateLimit(string slug, Guid anonymousId)
        => $"{slug}:widget:rate:{anonymousId:D}";

    public static string IdempotencyKey(string slug, Guid conversationId, Guid clientMessageId)
        => $"{slug}:widget:idem:{conversationId:D}:{clientMessageId:D}";

    public static string StartConversationLock(string slug, Guid anonymousId)
        => $"{slug}:widget:start:{anonymousId:D}";
}
