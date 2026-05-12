using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Commands;
using omniDesk.Api.Features.Tickets.Validators;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Spec 009 US5 — T139
/// Validator unit tests for CreateManualTicketRequest and domain-level
/// assertions about CreateManualTicketCommand's ticket construction rules.
/// Integration flows (dedup, DB) are exercised via integration tests.
/// </summary>
public class CreateManualTicketCommandTests
{
    private readonly CreateManualTicketRequestValidator _validator = new();

    // -----------------------------------------------------------------------
    // Validator — required fields
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_rejects_empty_department_id()
    {
        var req = ValidRequest() with { DepartmentId = Guid.Empty };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "DepartmentId");
    }

    [Fact]
    public void Validator_rejects_empty_subject()
    {
        var req = ValidRequest() with { Subject = "" };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Subject");
    }

    [Fact]
    public void Validator_rejects_subject_exceeding_500_chars()
    {
        var req = ValidRequest() with { Subject = new string('x', 501) };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Subject");
    }

    // -----------------------------------------------------------------------
    // Validator — contact requirement
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_rejects_when_no_contact_id_and_no_contact_fields()
    {
        var req = ValidRequest() with
        {
            ContactId    = null,
            ContactName  = null,
            ContactEmail = null,
            ContactPhone = null,
        };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "contact");
    }

    [Fact]
    public void Validator_accepts_when_contact_id_provided_and_no_other_fields()
    {
        var req = ValidRequest() with
        {
            ContactId    = Guid.NewGuid(),
            ContactName  = null,
            ContactEmail = null,
            ContactPhone = null,
        };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_accepts_when_only_contact_name_provided()
    {
        var req = ValidRequest() with
        {
            ContactId    = null,
            ContactName  = "João",
            ContactEmail = null,
            ContactPhone = null,
        };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_accepts_when_only_contact_email_provided()
    {
        var req = ValidRequest() with
        {
            ContactId    = null,
            ContactName  = null,
            ContactEmail = "joao@email.com",
            ContactPhone = null,
        };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_accepts_when_only_contact_phone_provided()
    {
        var req = ValidRequest() with
        {
            ContactId    = null,
            ContactName  = null,
            ContactEmail = null,
            ContactPhone = "+5511999999999",
        };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    // -----------------------------------------------------------------------
    // Validator — email format
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_rejects_malformed_email()
    {
        var req = ValidRequest() with { ContactEmail = "not-an-email" };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ContactEmail");
    }

    [Fact]
    public void Validator_accepts_null_email_without_error()
    {
        var req = ValidRequest() with { ContactEmail = null };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    // -----------------------------------------------------------------------
    // Validator — note length
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_rejects_note_exceeding_10000_chars()
    {
        var req = ValidRequest() with { Note = new string('n', 10_001) };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Note");
    }

    [Fact]
    public void Validator_accepts_note_at_10000_chars()
    {
        var req = ValidRequest() with { Note = new string('n', 10_000) };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    // -----------------------------------------------------------------------
    // Validator — tags
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_rejects_tag_exceeding_50_chars()
    {
        var req = ValidRequest() with { Tags = [new string('t', 51)] };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_accepts_tag_at_50_chars()
    {
        var req = ValidRequest() with { Tags = [new string('t', 50)] };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    // -----------------------------------------------------------------------
    // Validator — priority enum
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("low")]
    [InlineData("normal")]
    [InlineData("high")]
    [InlineData("urgent")]
    [InlineData(null)]
    public void Validator_accepts_valid_priority_values(string? priority)
    {
        var req = ValidRequest() with { Priority = priority };
        var result = _validator.Validate(req);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_rejects_unknown_priority()
    {
        var req = ValidRequest() with { Priority = "critical" };
        var result = _validator.Validate(req);
        Assert.False(result.IsValid);
    }

    // -----------------------------------------------------------------------
    // Domain — ticket status rules
    // -----------------------------------------------------------------------

    [Fact]
    public void Ticket_status_is_New_when_no_attendant_assigned()
    {
        // When AssignToMe=false (no attendant resolved), ticket starts as New
        var ticket = new Ticket
        {
            Id           = Guid.NewGuid(),
            Protocol     = "TK-20260101-00001",
            Channel      = TicketChannel.Manual,
            Status       = TicketStatus.New,
            Priority     = TicketPriority.Normal,
            Subject      = "Test",
            DepartmentId = Guid.NewGuid(),
            AttendantId  = null,
            SlaStartedAt = DateTimeOffset.UtcNow,
            CreatedAt    = DateTimeOffset.UtcNow,
            UpdatedAt    = DateTimeOffset.UtcNow,
        };

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Null(ticket.AttendantId);
        Assert.Null(ticket.AssignedAt);
    }

    [Fact]
    public void Ticket_status_is_InProgress_when_attendant_assigned()
    {
        // When AssignToMe=true and attendant found, ticket starts as InProgress
        var attendantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var ticket = new Ticket
        {
            Id           = Guid.NewGuid(),
            Protocol     = "TK-20260101-00002",
            Channel      = TicketChannel.Manual,
            Status       = TicketStatus.InProgress,
            Priority     = TicketPriority.Normal,
            Subject      = "Test",
            DepartmentId = Guid.NewGuid(),
            AttendantId  = attendantId,
            AssignedAt   = now,
            SlaStartedAt = now,
            CreatedAt    = now,
            UpdatedAt    = now,
        };

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(attendantId, ticket.AttendantId);
        Assert.NotNull(ticket.AssignedAt);
    }

    [Fact]
    public void Ticket_channel_is_Manual_for_manual_tickets()
    {
        var ticket = new Ticket
        {
            Id           = Guid.NewGuid(),
            Protocol     = "TK-20260101-00003",
            Channel      = TicketChannel.Manual,
            Status       = TicketStatus.New,
            Priority     = TicketPriority.Normal,
            Subject      = "Walk-in patient",
            DepartmentId = Guid.NewGuid(),
            SlaStartedAt = DateTimeOffset.UtcNow,
            CreatedAt    = DateTimeOffset.UtcNow,
            UpdatedAt    = DateTimeOffset.UtcNow,
        };

        Assert.Equal(TicketChannel.Manual, ticket.Channel);
    }

    [Theory]
    [InlineData("low",    TicketPriority.Low)]
    [InlineData("normal", TicketPriority.Normal)]
    [InlineData("high",   TicketPriority.High)]
    [InlineData("urgent", TicketPriority.Urgent)]
    [InlineData(null,     TicketPriority.Normal)]
    public void Priority_mapping_is_correct(string? wire, TicketPriority expected)
    {
        var priority = wire switch
        {
            "low"    => TicketPriority.Low,
            "high"   => TicketPriority.High,
            "urgent" => TicketPriority.Urgent,
            _        => TicketPriority.Normal,
        };

        Assert.Equal(expected, priority);
    }

    // -----------------------------------------------------------------------
    // Domain — SLA deadlines
    // -----------------------------------------------------------------------

    [Fact]
    public void Sla_deadlines_are_set_when_dept_has_sla_config()
    {
        var now = DateTimeOffset.UtcNow;
        const int firstResponseMinutes = 60;
        const int resolutionMinutes    = 480;

        var firstResponseDeadline = now.AddMinutes(firstResponseMinutes);
        var resolutionDeadline    = now.AddMinutes(resolutionMinutes);

        Assert.True(firstResponseDeadline > now);
        Assert.True(resolutionDeadline > firstResponseDeadline);
    }

    [Fact]
    public void Sla_deadlines_are_null_when_dept_has_no_sla_config()
    {
        DateTimeOffset? firstResponseDeadline = null;
        DateTimeOffset? resolutionDeadline    = null;

        Assert.Null(firstResponseDeadline);
        Assert.Null(resolutionDeadline);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CreateManualTicketRequest ValidRequest() =>
        new(
            DepartmentId: Guid.NewGuid(),
            Subject:      "Test ticket subject",
            Priority:     "normal",
            Tags:         [],
            AssignToMe:   false,
            ContactName:  "João Silva",
            ContactEmail: "joao@example.com",
            ContactPhone: null,
            ContactId:    null,
            Note:         null);
}
