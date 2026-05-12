using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Infrastructure.AgentRuntime;

namespace omniDesk.Api.Features.Contacts.Commands;

public record CreateContactRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Notes);

/// <summary>
/// Spec 009 US6 — T145.
/// Creates a contact using ContactDeduplicationService (find-or-create by email/phone).
/// </summary>
public class CreateContactCommand(
    ContactDeduplicationService dedup,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(object Data, string? Error)> ExecuteAsync(
        CreateContactRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)
            && string.IsNullOrWhiteSpace(req.Email)
            && string.IsNullOrWhiteSpace(req.Phone))
            return (null!, "CONTACT_NO_IDENTIFIER");

        var slug = slugAccessor.Slug;

        var contact = await dedup.FindOrCreateAsync(
            slug,
            new ContactDeduplicationService.ContactHints(
                Email:   req.Email,
                Phone:   req.Phone,
                Name:    req.Name,
                Channel: ContactSourceChannel.Manual),
            ct);

        return (new
        {
            id         = contact.Id,
            name       = contact.Name,
            email      = contact.Email,
            phone      = contact.Phone,
            created_at = contact.CreatedAt,
        }, null);
    }
}
