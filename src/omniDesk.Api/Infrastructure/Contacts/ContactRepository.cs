using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Contacts;

public class ContactRepository(AppDbContext db) : IContactRepository
{
    public async Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Contacts.FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);

    public async Task<Contact?> FindByEmailAsync(string emailLower, CancellationToken ct) =>
        await db.Contacts
            .Where(c => c.DeletedAt == null && c.Email != null && c.Email.ToLower() == emailLower)
            .FirstOrDefaultAsync(ct);

    public async Task<Contact?> FindByPhoneNormalizedAsync(string phoneNormalized, CancellationToken ct) =>
        await db.Contacts
            .Where(c => c.DeletedAt == null && c.PhoneNormalized == phoneNormalized)
            .FirstOrDefaultAsync(ct);

    public async Task<Contact> AddAsync(Contact contact, CancellationToken ct)
    {
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return contact;
    }

    public async Task UpdateAsync(Contact contact, CancellationToken ct)
    {
        contact.UpdatedAt = DateTimeOffset.UtcNow;
        db.Contacts.Update(contact);
        await db.SaveChangesAsync(ct);
    }
}
