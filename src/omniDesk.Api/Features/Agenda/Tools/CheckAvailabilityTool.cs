using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Features.Agenda.Availability;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Tools;

/// <summary>
/// Spec 011 T114 — implements check_availability AI tool call (research §R1: same
/// IAvailabilityCalculator as GET /api/availability REST endpoint).
/// </summary>
public sealed class CheckAvailabilityTool(IAvailabilityCalculator calculator, AppDbContext db)
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    public async Task<string> ExecuteAsync(string argumentsJson, ToolDispatchContext context, CancellationToken ct)
    {
        JsonElement args;
        try { args = JsonDocument.Parse(argumentsJson).RootElement; }
        catch { return Err("INVALID_ARGUMENTS"); }

        if (!TryGetGuid(args, "professional_id", out var professionalId))
            return Err(AgendaErrorCodes.ProfessionalNotFound);
        if (!TryGetGuid(args, "service_id", out var serviceId))
            return Err(AgendaErrorCodes.ServiceNotFound);
        if (!args.TryGetProperty("date", out var dateEl) || !DateOnly.TryParse(dateEl.GetString(), out var date))
            return Err(AgendaErrorCodes.InvalidDateFormat);

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == context.TenantId)
            .Select(t => new { t.Timezone })
            .FirstOrDefaultAsync(ct);
        var timezone = tenant?.Timezone ?? "America/Sao_Paulo";

        var svc = await db.Services.AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Select(s => new { s.DurationMinutes, s.IsActive })
            .FirstOrDefaultAsync(ct);
        if (svc is null || !svc.IsActive) return Err(AgendaErrorCodes.ServiceNotFound);

        var slots = await calculator.GetSlotsAsync(professionalId, serviceId, date, timezone, ct);

        return JsonSerializer.Serialize(new
        {
            professional_id = professionalId,
            service_id = serviceId,
            date = date.ToString("yyyy-MM-dd"),
            duration_minutes = svc.DurationMinutes,
            slots = slots.Select(s => new { start_at = s.StartAt, end_at = s.EndAt }),
        }, Opts);
    }

    private static bool TryGetGuid(JsonElement root, string property, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(property, out var el)
               && el.ValueKind == JsonValueKind.String
               && Guid.TryParse(el.GetString(), out value);
    }

    private static string Err(string code) => JsonSerializer.Serialize(new { error = code }, Opts);
}
