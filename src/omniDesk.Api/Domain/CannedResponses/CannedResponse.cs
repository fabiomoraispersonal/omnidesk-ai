namespace omniDesk.Api.Domain.CannedResponses;

public class CannedResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class CannedResponseVariable
{
    public const string ClientName = "client_name";
    public const string AttendantName = "attendant_name";
    public const string TicketNumber = "ticket_number";
    public const string DepartmentName = "department_name";

    public static readonly IReadOnlyList<string> All =
        [ClientName, AttendantName, TicketNumber, DepartmentName];

    public static readonly IReadOnlyDictionary<string, string> Fallbacks =
        new Dictionary<string, string>
        {
            [ClientName] = "cliente",
            [AttendantName] = "atendente",
            [TicketNumber] = "—",
            [DepartmentName] = "atendimento",
        };
}
