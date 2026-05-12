using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Factory helpers for domain objects used in Spec 009 unit tests.
/// All values are pre-populated with sensible defaults so individual tests
/// only need to specify the fields they care about.
/// </summary>
public static class TicketTestHelpers
{
    public static Ticket CreateTicket(
        Guid? departmentId = null,
        Guid? attendantId = null,
        Guid? contactId = null,
        TicketStatus status = TicketStatus.New,
        TicketChannel channel = TicketChannel.LiveChat,
        TicketPriority priority = TicketPriority.Normal,
        string subject = "Test ticket",
        string? protocol = null,
        DateTimeOffset? createdAt = null)
        => new Ticket
        {
            Id           = Guid.NewGuid(),
            Protocol     = protocol ?? "TK-20260101-00001",
            Channel      = channel,
            Status       = status,
            Priority     = priority,
            Subject      = subject,
            DepartmentId = departmentId ?? Guid.NewGuid(),
            AttendantId  = attendantId,
            ContactId    = contactId,
            SlaStartedAt = DateTimeOffset.UtcNow,
            CreatedAt    = createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAt    = DateTimeOffset.UtcNow,
        };

    public static Contact CreateContact(
        string? email = null,
        string? phone = null,
        string? name = "Test Contact")
        => new Contact
        {
            Id              = Guid.NewGuid(),
            Name            = name,
            Email           = email,
            Phone           = phone,
            PhoneNormalized = phone,
            SourceChannels  = [],
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow,
        };
}
