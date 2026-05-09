namespace omniDesk.Api.Domain.AiAgents;

public static class AgentVariableNames
{
    public const string CompanyName = "company_name";
    public const string DepartmentName = "department_name";
    public const string AttendantName = "attendant_name";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CompanyName,
        DepartmentName,
        AttendantName,
    };
}
