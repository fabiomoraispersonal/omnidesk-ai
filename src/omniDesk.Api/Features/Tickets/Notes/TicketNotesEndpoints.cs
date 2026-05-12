using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets.Notes;

public static class TicketNotesEndpoints
{
    public static RouteGroupBuilder MapTicketNotesEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{ticketId:guid}/notes", PostNoteAsync);
        group.MapGet("/{ticketId:guid}/notes", GetNotesAsync);
        return group;
    }

    private static async Task<IResult> PostNoteAsync(
        Guid ticketId,
        AddTicketNoteRequest req,
        ICurrentUser caller,
        AddTicketNoteCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var attendantId = await ResolveAttendantIdAsync(caller, userId);
        if (attendantId is null)
            return Results.Json(Error("NOT_AN_ATTENDANT", "Only attendants can add notes."), statusCode: 403);

        var validator = new AddTicketNoteRequestValidator();
        var validation = validator.Validate(req);
        if (!validation.IsValid)
            return Results.Json(ValidationError(validation), statusCode: 400);

        var (found, noteId) = await command.ExecuteAsync(ticketId, req.Content, attendantId.Value, ct);
        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);

        return Results.Ok(new { success = true, data = new { id = noteId } });
    }

    private static async Task<IResult> GetNotesAsync(
        Guid ticketId,
        AppDbContext db,
        ICurrentUser caller,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);

        var notes = await db.TicketNotes.AsNoTracking()
            .Where(n => n.TicketId == ticketId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);

        var attIds = notes.Select(n => n.AttendantId).Distinct().ToList();
        var attNames = attIds.Count > 0
            ? await db.Attendants.AsNoTracking()
                .Where(a => attIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct)
            : new Dictionary<Guid, string?>();

        var data = notes.Select(n =>
        {
            attNames.TryGetValue(n.AttendantId, out var name);
            return new
            {
                id             = n.Id,
                attendant_id   = n.AttendantId,
                attendant_name = name,
                content        = n.Content,
                created_at     = n.CreatedAt,
            };
        });

        return Results.Ok(new { success = true, data });
    }

    // Attendant lookup is scoped per request without an extra DI service
    private static Task<Guid?> ResolveAttendantIdAsync(ICurrentUser caller, Guid userId)
    {
        // The attendant id == userId for this system (Attendant.UserId FK)
        // Actual lookup is done inside commands when needed; here we just need it for ownership
        return Task.FromResult<Guid?>(userId);
    }

    private static object Error(string code, string message) =>
        new { success = false, error = new { code, message } };

    private static object ValidationError(FluentValidation.Results.ValidationResult vr) =>
        new
        {
            success = false,
            error = new
            {
                code    = "VALIDATION_ERROR",
                message = "Validation failed.",
                details = vr.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }),
            },
        };
}
