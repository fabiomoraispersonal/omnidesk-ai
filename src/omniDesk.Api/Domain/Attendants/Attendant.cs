namespace omniDesk.Api.Domain.Attendants;

public class Attendant
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int MaxSimultaneousChats { get; set; } = 5;
    public int ActiveTicketCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AttendantDepartment> Departments { get; set; } = new List<AttendantDepartment>();
    public AttendantStatusEntry? Status { get; set; }
}

public class AttendantDepartment
{
    public Guid AttendantId { get; set; }
    public Guid DepartmentId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Attendant? Attendant { get; set; }
    public Domain.Departments.Department? Department { get; set; }
}

public class AttendantStatusEntry
{
    public Guid AttendantId { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Offline;
    public DateTimeOffset ChangedAt { get; set; }
    public AttendanceStatusChangedBy ChangedBy { get; set; } = AttendanceStatusChangedBy.Manual;
    public DateTimeOffset? LastHeartbeatAt { get; set; }
}

public interface IAttendantRepository
{
    Task<Attendant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Attendant?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Attendant>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<Attendant>> ListByDepartmentAsync(Guid departmentId, CancellationToken ct = default);
    Task AddAsync(Attendant attendant, CancellationToken ct = default);
    Task UpdateAsync(Attendant attendant, CancellationToken ct = default);
    Task ReplaceDepartmentsAsync(Guid attendantId, IReadOnlyList<Guid> departmentIds, Guid primaryDepartmentId, CancellationToken ct = default);
}
