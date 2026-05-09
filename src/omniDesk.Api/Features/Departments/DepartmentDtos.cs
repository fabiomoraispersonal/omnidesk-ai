namespace omniDesk.Api.Features.Departments;

public record BusinessHoursDto(string Start, string End, int[] Days);

public record SlaDto(int? FirstResponseMinutes, int? ResolutionMinutes);

public record CreateDepartmentRequest(
    string Name,
    string? Description,
    BusinessHoursDto? BusinessHours,
    SlaDto? Sla);

public record UpdateDepartmentRequest(
    string Name,
    string? Description,
    BusinessHoursDto? BusinessHours,
    SlaDto? Sla);

public record DepartmentResponse(
    Guid Id,
    string Name,
    string? Description,
    BusinessHoursDto? BusinessHours,
    SlaDto Sla,
    bool IsActive,
    int AttendantCount,
    int ActiveTicketCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
