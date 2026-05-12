using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Notes;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets.Notes;

/// <summary>
/// Unit tests for AddTicketNoteCommand domain logic:
/// - Validator (content length bounds)
/// - Note entity construction
/// - Mongo event carries only note_id (no content)
/// </summary>
public class AddTicketNoteCommandTests
{
    private readonly AddTicketNoteRequestValidator _validator = new();

    [Fact]
    public void Validator_rejects_empty_content()
    {
        var result = _validator.Validate(new AddTicketNoteRequest(""));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_rejects_whitespace_only()
    {
        var result = _validator.Validate(new AddTicketNoteRequest("   "));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_accepts_one_char_content()
    {
        var result = _validator.Validate(new AddTicketNoteRequest("X"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_accepts_max_length_content()
    {
        var content = new string('A', 10_000);
        var result = _validator.Validate(new AddTicketNoteRequest(content));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_rejects_over_max_length_content()
    {
        var content = new string('A', 10_001);
        var result = _validator.Validate(new AddTicketNoteRequest(content));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NoteEntity_has_correct_defaults()
    {
        var ticketId    = Guid.NewGuid();
        var attendantId = Guid.NewGuid();
        var content     = "Cliente pediu desconto.";
        var now         = DateTimeOffset.UtcNow;

        var note = new TicketNote
        {
            Id          = Guid.NewGuid(),
            TicketId    = ticketId,
            AttendantId = attendantId,
            Content     = content,
            CreatedAt   = now,
        };

        Assert.Equal(ticketId, note.TicketId);
        Assert.Equal(attendantId, note.AttendantId);
        Assert.Equal(content, note.Content);
        Assert.Equal(now, note.CreatedAt);
    }

    [Fact]
    public void MongoEvent_NoteAdded_carries_noteId_not_content()
    {
        var noteId     = Guid.NewGuid();
        var ticketId   = Guid.NewGuid();
        var actorId    = Guid.NewGuid();
        var tenantSlug = "clinic-abc";
        var now        = DateTimeOffset.UtcNow;

        var ev = new TicketEvent(
            TenantSlug: tenantSlug,
            TicketId: ticketId,
            Protocol: "TK-20260511-00001",
            EventType: TicketEventType.NoteAdded,
            ActorType: "attendant",
            Timestamp: now)
        {
            ActorId = actorId,
            NoteId  = noteId,
        };

        Assert.Equal(TicketEventType.NoteAdded, ev.EventType);
        Assert.Equal(noteId, ev.NoteId);
        Assert.Null(ev.From);  // content NOT in the event — privacy-safe
        Assert.Null(ev.To);
    }

    [Fact]
    public void FakeEventStore_captures_note_added_event()
    {
        var store = new FakeTicketEventStore();
        var ev = new TicketEvent(
            "test-slug", Guid.NewGuid(), "TK-x", TicketEventType.NoteAdded, "attendant", DateTimeOffset.UtcNow)
        {
            NoteId = Guid.NewGuid()
        };

        store.AppendAsync(ev, CancellationToken.None);

        Assert.Single(store.Events);
        Assert.NotNull(store.FirstOfType(TicketEventType.NoteAdded));
    }
}
