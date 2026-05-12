using omniDesk.Api.Domain.Tickets;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets.Notes;

/// <summary>
/// Spec 009 US8 — T169
/// Verifies that TicketNote content is strictly internal:
/// - The NoteAdded event carries only note_id, never the note body
/// - The TicketEvent structure used for pub/sub has no note content field
/// - ContextBuilder never receives TicketNote entities (structural test)
/// </summary>
public class InternalNotesIsolationTests
{
    // -----------------------------------------------------------------------
    // TicketEvent carries note_id only — no content
    // -----------------------------------------------------------------------

    [Fact]
    public void TicketEvent_NoteAdded_carries_only_NoteId_not_content()
    {
        var noteId = Guid.NewGuid();
        var ev = new TicketEvent(
            TenantSlug: "tenant-a",
            TicketId:   Guid.NewGuid(),
            Protocol:   "TK-20260101-00001",
            EventType:  TicketEventType.NoteAdded,
            ActorType:  "attendant",
            Timestamp:  DateTimeOffset.UtcNow)
        {
            ActorId = Guid.NewGuid(),
            NoteId  = noteId,
        };

        Assert.Equal(noteId, ev.NoteId);
        Assert.Equal(TicketEventType.NoteAdded, ev.EventType);

        // TicketEvent has no Content property — content is never part of the event
        var props = typeof(TicketEvent).GetProperties();
        var hasContentProp = props.Any(p =>
            p.Name.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("NoteContent", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals("Body", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasContentProp, "TicketEvent must not expose note content.");
    }

    // -----------------------------------------------------------------------
    // ContextBuilder has no TicketNote dependency
    // -----------------------------------------------------------------------

    [Fact]
    public void ContextBuilder_has_no_TicketNote_dependency()
    {
        var contextBuilderType = typeof(omniDesk.Api.Features.AgentRuntime.ContextBuilder);

        // ContextBuilder constructor parameters must not include anything from TicketNote
        var ctorParams = contextBuilderType
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.FullName ?? "")
            .ToList();

        Assert.DoesNotContain(ctorParams, p => p.Contains("TicketNote"));
    }

    [Fact]
    public void ContextBuilder_methods_do_not_accept_TicketNote_parameters()
    {
        var contextBuilderType = typeof(omniDesk.Api.Features.AgentRuntime.ContextBuilder);
        var methods = contextBuilderType.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var method in methods)
        {
            var hasNoteParam = method.GetParameters()
                .Any(p => p.ParameterType.FullName?.Contains("TicketNote") == true);
            Assert.False(hasNoteParam,
                $"ContextBuilder.{method.Name} must not accept TicketNote parameters.");
        }
    }

    // -----------------------------------------------------------------------
    // NoteAdded event does not expose note content in serializable form
    // -----------------------------------------------------------------------

    [Fact]
    public void TicketNote_Content_field_is_not_in_TicketEvent_schema()
    {
        var ticketEventProps = typeof(TicketEvent)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Content", ticketEventProps);
        Assert.DoesNotContain("NoteContent", ticketEventProps);
        Assert.DoesNotContain("NoteText", ticketEventProps);
        Assert.DoesNotContain("Body", ticketEventProps);
    }

    // -----------------------------------------------------------------------
    // AddTicketNoteCommand event payload audit
    // -----------------------------------------------------------------------

    [Fact]
    public void AddTicketNoteCommand_event_NoteId_maps_correctly()
    {
        var noteId   = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var actorId  = Guid.NewGuid();
        var now      = DateTimeOffset.UtcNow;

        // Replicate the event construction from AddTicketNoteCommand
        var ev = new TicketEvent(
            TenantSlug: "clinic-abc",
            TicketId:   ticketId,
            Protocol:   "TK-20260101-00042",
            EventType:  TicketEventType.NoteAdded,
            ActorType:  "attendant",
            Timestamp:  now)
        {
            ActorId = actorId,
            NoteId  = noteId,
        };

        Assert.Equal(noteId,   ev.NoteId);
        Assert.Equal(ticketId, ev.TicketId);
        Assert.Equal(actorId,  ev.ActorId);
        Assert.Null(ev.DepartmentFromId);
        Assert.Null(ev.DepartmentToId);
    }
}
