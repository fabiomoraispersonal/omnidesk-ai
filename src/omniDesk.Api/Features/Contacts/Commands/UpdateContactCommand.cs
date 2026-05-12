using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Contacts.Commands;

public record UpdateContactRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Notes);

/// <summary>
/// Spec 009 US6 — T146.
/// Updates a contact's editable fields. Recalculates phone_normalized when phone changes.
/// Returns EMAIL_CONFLICT or PHONE_CONFLICT (409) if unique constraints are violated.
/// </summary>
public class UpdateContactCommand(AppDbContext db)
{
    public async Task<(bool Found, string? Error)> ExecuteAsync(
        Guid id,
        UpdateContactRequest req,
        CancellationToken ct)
    {
        var contact = await db.Contacts
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);

        if (contact is null)
            return (false, null);

        // Check for email conflict
        if (req.Email is not null && req.Email != contact.Email)
        {
            var emailConflict = await db.Contacts.AnyAsync(
                c => c.Id != id && c.DeletedAt == null &&
                     c.Email != null && c.Email.ToLower() == req.Email.ToLower(), ct);

            if (emailConflict)
                return (true, "EMAIL_CONFLICT");
        }

        // Normalize new phone and check for conflict
        string? newPhoneNormalized = null;
        if (req.Phone is not null && req.Phone != contact.Phone)
        {
            newPhoneNormalized = PhoneNormalizer.Normalize(req.Phone);

            if (newPhoneNormalized is not null)
            {
                var phoneConflict = await db.Contacts.AnyAsync(
                    c => c.Id != id && c.DeletedAt == null &&
                         c.PhoneNormalized == newPhoneNormalized, ct);

                if (phoneConflict)
                    return (true, "PHONE_CONFLICT");
            }
        }

        if (req.Name  is not null) contact.Name  = req.Name;
        if (req.Email is not null) contact.Email = req.Email;
        if (req.Notes is not null) contact.Notes = req.Notes;

        if (req.Phone is not null)
        {
            contact.Phone           = req.Phone;
            contact.PhoneNormalized = newPhoneNormalized ?? PhoneNormalizer.Normalize(req.Phone);
        }

        contact.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return (true, null);
    }
}
