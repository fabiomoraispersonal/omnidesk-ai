namespace omniDesk.Api.Domain.Contacts;

public interface IContactRepository
{
    Task<Contact?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Contact?> FindByEmailAsync(string emailLower, CancellationToken ct);
    Task<Contact?> FindByPhoneNormalizedAsync(string phoneNormalized, CancellationToken ct);
    Task<Contact> AddAsync(Contact contact, CancellationToken ct);
    Task UpdateAsync(Contact contact, CancellationToken ct);
}
