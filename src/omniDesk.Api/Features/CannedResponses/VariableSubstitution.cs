using System.Text.RegularExpressions;
using omniDesk.Api.Domain.CannedResponses;

namespace omniDesk.Api.Features.CannedResponses;

public record SubstitutionContext(
    string? ClientName,
    string? AttendantName,
    long? TicketNumber,
    string? DepartmentName);

public record SubstitutionResult(string Rendered, IReadOnlyList<string> UnknownVariables);

/// <summary>
/// Pure regex-based substitution (research §R7).
/// - Known variables → substituted with context value or fallback (FR-034).
/// - Unknown variables → preserved literally; caller logs them as Warning (canned mal cadastrada).
/// </summary>
public static class VariableSubstitution
{
    private static readonly Regex Pattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static SubstitutionResult Apply(string template, SubstitutionContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return new SubstitutionResult(template ?? string.Empty, Array.Empty<string>());

        var unknown = new HashSet<string>(StringComparer.Ordinal);
        var rendered = Pattern.Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            switch (name)
            {
                case CannedResponseVariable.ClientName:
                    return Coalesce(ctx.ClientName, CannedResponseVariable.ClientName);
                case CannedResponseVariable.AttendantName:
                    return Coalesce(ctx.AttendantName, CannedResponseVariable.AttendantName);
                case CannedResponseVariable.TicketNumber:
                    return ctx.TicketNumber?.ToString() ?? CannedResponseVariable.Fallbacks[CannedResponseVariable.TicketNumber];
                case CannedResponseVariable.DepartmentName:
                    return Coalesce(ctx.DepartmentName, CannedResponseVariable.DepartmentName);
                default:
                    unknown.Add(name);
                    return m.Value; // preserve literally
            }
        });

        return new SubstitutionResult(rendered, unknown.ToArray());
    }

    private static string Coalesce(string? value, string variableName)
        => string.IsNullOrWhiteSpace(value)
            ? CannedResponseVariable.Fallbacks[variableName]
            : value;
}
