using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Professionals.Commands;

/// <summary>Spec 011 T056 — cria um novo profissional.</summary>
public class CreateProfessionalCommand(ProfessionalRepository repo)
{
    public async Task<Professional> ExecuteAsync(
        string name, string? specialty, Guid? departmentId, Guid? attendantId, CancellationToken ct)
    {
        var p = new Professional
        {
            Name = name, Specialty = specialty,
            DepartmentId = departmentId, AttendantId = attendantId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return await repo.AddAsync(p, ct);
    }
}
