using System.Text.RegularExpressions;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Notifications;

namespace omniDesk.Api.Features.Notifications;

/// <summary>Spec 010 Phase 9 T093 — tenant-admin REST surface for follow-up + reminder settings.</summary>
public static class TenantSettingsEndpoints
{
    public record UpdateSettingsRequest(
        bool FollowUpEnabled,
        bool ReminderEnabled,
        string ReminderTime);

    private static readonly Regex TimeRegex = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    public static RouteGroupBuilder MapTenantSettingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAsync).WithName("NotificationSettings_Get");
        group.MapPut("/",  PutAsync).WithName("NotificationSettings_Put");
        return group;
    }

    private static async Task<IResult> GetAsync(
        ICurrentUser current,
        TenantSettingsRepository repo,
        CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        if (current.TenantId is null) return Forbidden();

        var settings = await repo.GetAsync(current.TenantId.Value, ct);
        return Results.Ok(new
        {
            success = true,
            data = new
            {
                follow_up_enabled = settings.FollowUpEnabled,
                reminder_enabled  = settings.ReminderEnabled,
                reminder_time     = settings.ReminderTime.ToString("HH:mm"),
            },
        });
    }

    private static async Task<IResult> PutAsync(
        UpdateSettingsRequest request,
        ICurrentUser current,
        UpdateTenantSettingsCommand command,
        CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        if (current.TenantId is null) return Forbidden();

        if (string.IsNullOrWhiteSpace(request.ReminderTime) || !TimeRegex.IsMatch(request.ReminderTime))
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "INVALID_REMINDER_TIME",
                    message = "reminder_time must match HH:mm in 24h format.",
                },
            });
        }

        var time = TimeOnly.ParseExact(request.ReminderTime, "HH:mm");
        var settings = await command.ExecuteAsync(
            current.TenantId.Value,
            request.FollowUpEnabled,
            request.ReminderEnabled,
            time, ct);

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                follow_up_enabled = settings.FollowUpEnabled,
                reminder_enabled  = settings.ReminderEnabled,
                reminder_time     = settings.ReminderTime.ToString("HH:mm"),
            },
        });
    }

    private static bool IsTenantAdmin(ICurrentUser current) =>
        current.IsAuthenticated && current.Role == Roles.TenantAdmin;

    private static IResult Forbidden() =>
        Results.Json(new
        {
            success = false,
            error = new
            {
                code = "FORBIDDEN_ROLE",
                message = "Only tenant_admin can manage notification settings.",
            },
        }, statusCode: 403);
}
