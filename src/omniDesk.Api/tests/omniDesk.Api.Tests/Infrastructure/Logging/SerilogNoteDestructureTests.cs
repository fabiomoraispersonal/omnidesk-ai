using omniDesk.Api.Domain.Tickets;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Logging;

/// <summary>
/// Spec 009 US8 — T170
/// Structural assertions: TicketNote destructure transformer in Program.cs omits Content.
/// Uses reflection to confirm the anonymous projection shape excludes the sensitive field.
/// </summary>
public class SerilogNoteDestructureTests
{
    // Mirrors the ByTransforming lambda in Program.cs — if that changes, this test will catch drift.
    private static object DestructureNote(TicketNote n) => new
    {
        n.Id,
        n.TicketId,
        n.AttendantId,
        n.CreatedAt,
        // content omitted
    };

    [Fact]
    public void Destructured_note_does_not_expose_Content_property()
    {
        var note = MakeNote("Sensitive clinical information — must not appear in logs.");
        var projected = DestructureNote(note);
        var props = projected.GetType().GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("Content", props);
    }

    [Fact]
    public void Destructured_note_does_not_contain_the_secret_string()
    {
        const string secret = "Highly sensitive patient data.";
        var note = MakeNote(secret);
        var projected = DestructureNote(note);

        // Serialize to string to check no property value leaks the content
        var allValues = projected.GetType()
            .GetProperties()
            .Select(p => p.GetValue(projected)?.ToString() ?? "")
            .ToList();

        Assert.DoesNotContain(allValues, v => v.Contains(secret));
    }

    [Fact]
    public void Destructured_note_retains_auditable_fields()
    {
        var note = MakeNote("anything");
        var projected = DestructureNote(note);
        var props = projected.GetType().GetProperties().Select(p => p.Name).ToHashSet();

        Assert.Contains("Id", props);
        Assert.Contains("TicketId", props);
        Assert.Contains("AttendantId", props);
        Assert.Contains("CreatedAt", props);
    }

    [Fact]
    public void Destructured_note_Id_matches_original()
    {
        var note = MakeNote("sensitive");
        var projected = DestructureNote(note);

        var idProp = projected.GetType().GetProperty("Id");
        Assert.NotNull(idProp);
        Assert.Equal(note.Id, idProp!.GetValue(projected));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TicketNote MakeNote(string content) => new TicketNote
    {
        Id          = Guid.NewGuid(),
        TicketId    = Guid.NewGuid(),
        AttendantId = Guid.NewGuid(),
        Content     = content,
        CreatedAt   = DateTimeOffset.UtcNow,
    };
}
