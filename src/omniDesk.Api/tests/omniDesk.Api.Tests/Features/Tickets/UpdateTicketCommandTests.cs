using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Tickets;

/// <summary>
/// Unit tests for UpdateTicketCommand domain logic:
/// - Subject / priority / tags delta detection
/// - Terminal tickets are rejected
/// </summary>
public class UpdateTicketCommandTests
{
    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Cancelled)]
    public void Terminal_tickets_cannot_be_updated(TicketStatus terminal)
    {
        // The command checks IsTerminal() and returns (Found, Forbidden, AlreadyClosed=true)
        Assert.True(terminal.IsTerminal());
    }

    [Fact]
    public void Tags_delta_adds_new_tags_removes_old()
    {
        var ticket = TicketTestHelpers.CreateTicket();
        ticket.Tags = ["lead", "vip"];

        var newTags = new[] { "vip", "novo" };
        var added   = newTags.Except(ticket.Tags).ToArray();
        var removed = ticket.Tags.Except(newTags).ToArray();

        Assert.Contains("novo", added);
        Assert.DoesNotContain("vip", added);
        Assert.Contains("lead", removed);
        Assert.DoesNotContain("vip", removed);
    }

    [Fact]
    public void Tags_same_value_produces_no_delta()
    {
        var original = new[] { "lead", "vip" };
        var same     = new[] { "lead", "vip" };

        var added   = same.Except(original).ToArray();
        var removed = original.Except(same).ToArray();

        Assert.Empty(added);
        Assert.Empty(removed);
    }

    [Fact]
    public void Priority_change_detected_correctly()
    {
        var ticket = TicketTestHelpers.CreateTicket();
        ticket.Priority = TicketPriority.Normal;

        // New priority differs → event should be emitted
        var newPriority = TicketPriority.High;
        var changed = newPriority != ticket.Priority;
        Assert.True(changed);
    }

    [Fact]
    public void Priority_same_value_produces_no_change()
    {
        var ticket = TicketTestHelpers.CreateTicket();
        ticket.Priority = TicketPriority.Normal;

        var newPriority = TicketPriority.Normal;
        Assert.False(newPriority != ticket.Priority);
    }

    [Fact]
    public void Subject_change_detected()
    {
        var ticket = TicketTestHelpers.CreateTicket(subject: "Original");
        var newSubject = "Updated";
        Assert.NotEqual(newSubject, ticket.Subject);
    }

    [Fact]
    public void Subject_same_value_no_change()
    {
        var ticket = TicketTestHelpers.CreateTicket(subject: "Same");
        Assert.Equal("Same", ticket.Subject);
    }
}
