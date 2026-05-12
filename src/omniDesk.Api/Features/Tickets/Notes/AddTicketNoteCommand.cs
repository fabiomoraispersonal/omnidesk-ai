using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets.Notes;

public record AddTicketNoteRequest(string Content);

public class AddTicketNoteRequestValidator : AbstractValidator<AddTicketNoteRequest>
{
    public AddTicketNoteRequestValidator()
    {
        RuleFor(r => r.Content)
            .NotEmpty()
            .MaximumLength(10_000);
    }
}

public class AddTicketNoteCommand(
    AppDbContext db,
    ITicketEventStore eventStore,
    ITenantSlugAccessor slugAccessor)
{
    public async Task<(bool Found, Guid? NoteId)> ExecuteAsync(
        Guid ticketId,
        string content,
        Guid actorId,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.DeletedAt == null, ct);
        if (ticket is null)
            return (false, null);

        var now = DateTimeOffset.UtcNow;
        var note = new TicketNote
        {
            Id          = Guid.NewGuid(),
            TicketId    = ticketId,
            AttendantId = actorId,
            Content     = content,
            CreatedAt   = now,
        };
        db.TicketNotes.Add(note);
        await db.SaveChangesAsync(ct);

        try
        {
            await eventStore.AppendAsync(new TicketEvent(
                TenantSlug: slugAccessor.Slug,
                TicketId: ticketId,
                Protocol: ticket.Protocol,
                EventType: TicketEventType.NoteAdded,
                ActorType: "attendant",
                Timestamp: now)
            {
                ActorId = actorId,
                NoteId  = note.Id,
            }, ct);
        }
        catch (Exception)
        {
            // Best-effort
        }

        return (true, note.Id);
    }
}
