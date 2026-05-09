namespace omniDesk.Api.Features.Attendants;

public record CreateAttendantRequest(
    Guid UserId,
    string Name,
    int? MaxSimultaneousChats,
    Guid[] DepartmentIds,
    Guid? PrimaryDepartmentId);

public record UpdateAttendantRequest(
    string Name,
    int? MaxSimultaneousChats);

public record UpdateAttendantDepartmentsRequest(
    Guid[] DepartmentIds,
    Guid? PrimaryDepartmentId);

public record UpdateAttendantStatusRequest(string Status);

public record AttendantResponse(
    Guid Id,
    Guid UserId,
    string Name,
    string? AvatarUrl,
    int MaxSimultaneousChats,
    int ActiveTicketCount,
    bool IsActive,
    Guid[] DepartmentIds,
    Guid? PrimaryDepartmentId,
    string? Status,
    DateTimeOffset CreatedAt);
