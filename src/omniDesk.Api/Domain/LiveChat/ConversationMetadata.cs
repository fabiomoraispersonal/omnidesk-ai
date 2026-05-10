using System.Net;

namespace omniDesk.Api.Domain.LiveChat;

/// <summary>
/// Origin metadata captured at conversation start. Stored as JSONB on conversations.metadata.
/// IP is stored partially (3 first IPv4 octets or /48 IPv6 prefix) per FR-021 / Constitution §IV.
/// </summary>
public record ConversationMetadata(
    string? PageUrl,
    string? PageTitle,
    string? Referrer,
    string? UserAgent,
    string? IpPartial)
{
    public static string PartialIp(IPAddress? address)
    {
        if (address is null) return string.Empty;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0";
        }
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            // /48 prefix → first 6 bytes, rest zeroed
            for (var i = 6; i < bytes.Length; i++) bytes[i] = 0;
            return new IPAddress(bytes).ToString();
        }
        return string.Empty;
    }
}
