namespace omniDesk.Api.Domain.Departments;

public class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Stored as 3 separate columns; aggregated through DepartmentBusinessHours when read.
    public TimeOnly? BusinessHoursStart { get; set; }
    public TimeOnly? BusinessHoursEnd { get; set; }
    public int[]? BusinessDays { get; set; }

    public int? SlaFirstResponseMinutes { get; set; }
    public int? SlaResolutionMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public DepartmentBusinessHours? GetBusinessHours() =>
        DepartmentBusinessHours.Create(BusinessHoursStart, BusinessHoursEnd, BusinessDays);

    public void SetBusinessHours(DepartmentBusinessHours? hours)
    {
        BusinessHoursStart = hours?.Start;
        BusinessHoursEnd = hours?.End;
        BusinessDays = hours?.Days.ToArray();
    }
}

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId, CancellationToken ct = default);
    Task<bool> HasActiveTicketsAsync(Guid departmentId, CancellationToken ct = default);
    Task<bool> HasLinkedAttendantsAsync(Guid departmentId, CancellationToken ct = default);
    Task AddAsync(Department department, CancellationToken ct = default);
    Task UpdateAsync(Department department, CancellationToken ct = default);
}
