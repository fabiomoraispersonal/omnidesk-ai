using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.Contacts;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Infrastructure.WebSockets;
using Serilog;

namespace omniDesk.Api.Features.Tickets;

/// <summary>
/// Spec 009 — real implementation of ITicketCreationGateway.
/// Replaces StubTicketCreationGateway (Spec 006) with the full 11-step handoff flow.
/// </summary>
public class TicketCreationGateway(
    AppDbContext db,
    ITenantSlugAccessor slugAccessor,
    TicketProtocolService protocolService,
    TicketAssignmentService assignmentService,
    ContactDeduplicationService contactDedup,
    ITicketEventStore eventStore,
    TicketEventPublisher eventPublisher,
    INotificationService notifications) : ITicketCreationGateway
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<TicketCreationGateway>();

    public async Task<TicketHandoffResult> CreateTicketFromAiHandoffAsync(
        TicketHandoffRequest request,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tenantSlug = slugAccessor.Slug;
        var now = DateTimeOffset.UtcNow;

        // Step 1: Contact deduplication (R9 / FR-026)
        Guid? contactId = null;
        if (request.ContactHints is { } hints
            && (!string.IsNullOrWhiteSpace(hints.Email) || !string.IsNullOrWhiteSpace(hints.Phone)))
        {
            try
            {
                var contact = await contactDedup.FindOrCreateAsync(tenantSlug,
                    new ContactDeduplicationService.ContactHints(
                        hints.Email,
                        hints.Phone,
                        hints.Name,
                        MapContactChannel(request.Channel)),
                    ct);
                contactId = contact.Id;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex,
                    "Contact dedup failed for handoff (channel {Channel}); proceeding without contact.",
                    request.Channel.ToWireValue());
            }
        }

        // Step 2: Protocol generation (R1 / SC-004)
        var protocol = await protocolService.GenerateAsync(tenantSlug, ct);

        // Step 3: Department + SLA
        var dept = await db.Departments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, ct)
            ?? throw new TicketCreationException(
                "DEPARTMENT_NOT_FOUND",
                $"Department {request.DepartmentId} not found.");

        var subject = !string.IsNullOrWhiteSpace(request.SubjectSuggestion)
            ? request.SubjectSuggestion
            : TicketSubjectAutogen.Generate(
                request.History.LastOrDefault()?.Content,
                ChannelLabel(request.Channel));

        // Step 4: Build ticket
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Protocol = protocol,
            Channel = request.Channel,
            Status = TicketStatus.New,
            Priority = TicketPriority.Normal,
            Subject = subject,
            DepartmentId = request.DepartmentId,
            ConversationId = request.ConversationId == Guid.Empty ? null : request.ConversationId,
            ContactId = contactId,
            SlaStartedAt = now,
            SlaResolutionDeadline = dept.SlaResolutionMinutes.HasValue
                ? now.AddMinutes(dept.SlaResolutionMinutes.Value)
                : null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Transaction: INSERT ticket + snapshot + conversation link
        await using var txn = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(ct);

            // Step 6: ai_handoff_snapshots
            var historyJson = JsonSerializer.Serialize(
                request.History.Select(m => new { role = m.Role, content = m.Content, sent_at = m.SentAt }));
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO ai_handoff_snapshots (id, ticket_id, thread_id, history_json, created_at)
                   VALUES (gen_random_uuid(), {ticket.Id}, {request.ThreadId}, {historyJson}::jsonb, now())",
                ct);

            // Step 7: conversations.ticket_id (best-effort — 0 rows in stub-gateway scenario)
            if (request.ConversationId != Guid.Empty)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE conversations
                       SET ticket_id = {ticket.Id}, updated_at = now()
                       WHERE id = {request.ConversationId} AND ticket_id IS NULL",
                    ct);
            }

            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }

        // Step 5: Round-robin assignment (after commit so ticket is visible to other readers)
        await assignmentService.AssignAsync(tenantSlug,
            new AssignTicketRequest(ticket.Id, request.DepartmentId, AssignmentReason.AiHandoff), ct);

        // Refresh assigned state from EF tracker
        var finalTicket = await db.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id, ct);

        // Steps 8–9: best-effort side effects
        await AppendMongoEventsAsync(tenantSlug, finalTicket, request, ct);
        await PublishWebSocketEventsAsync(tenantSlug, finalTicket, request, ct);

        // Step 10: Notification (Spec 010 implements; V1 is no-op stub — T079)
        if (finalTicket.AttendantId.HasValue)
        {
            try
            {
                await notifications.NotifyTicketAssignedAsync(
                    finalTicket.AttendantId.Value, finalTicket.Id, finalTicket.Protocol!, ct);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Notification failed for ticket {TicketId}; ignored.", finalTicket.Id);
            }
        }

        Logger.Information(
            "TicketCreationGateway: created {Protocol} in dept {DeptName} " +
            "via handoff from agent {AgentId} thread {ThreadId}, " +
            "attendant {AttendantId}, contact {ContactId}, channel {Channel}. {Ms}ms.",
            finalTicket.Protocol, dept.Name,
            request.OriginatingAgentId, request.ThreadId,
            finalTicket.AttendantId?.ToString() ?? "fila",
            contactId?.ToString() ?? "anônimo",
            request.Channel.ToWireValue(), sw.ElapsedMilliseconds);

        return new TicketHandoffResult(
            TicketId: finalTicket.Id,
            Protocol: finalTicket.Protocol!,
            DepartmentId: dept.Id,
            DepartmentName: dept.Name,
            AttendantId: finalTicket.AttendantId,
            Status: finalTicket.Status.ToWireValue(),
            ContactId: contactId);
    }

    private async Task AppendMongoEventsAsync(
        string tenantSlug, Ticket ticket, TicketHandoffRequest request, CancellationToken ct)
    {
        try
        {
            await eventStore.AppendAsync(new TicketEvent(
                tenantSlug, ticket.Id, ticket.Protocol,
                TicketEventType.TicketCreated, "system", DateTimeOffset.UtcNow)
            {
                ActorId = request.OriginatingAgentId,
                Reason  = request.Reason,
            }, ct);

            if (ticket.AttendantId.HasValue)
            {
                await eventStore.AppendAsync(new TicketEvent(
                    tenantSlug, ticket.Id, ticket.Protocol,
                    TicketEventType.AttendantAssigned, "system", DateTimeOffset.UtcNow)
                {
                    AttendantToId  = ticket.AttendantId,
                    DepartmentToId = ticket.DepartmentId,
                }, ct);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to append Mongo events for ticket {TicketId}", ticket.Id);
        }
    }

    private async Task PublishWebSocketEventsAsync(
        string tenantSlug, Ticket ticket, TicketHandoffRequest request, CancellationToken ct)
    {
        try
        {
            await eventPublisher.PublishCreatedAsync(tenantSlug, ticket.DepartmentId, new
            {
                ticket_id    = ticket.Id,
                protocol     = ticket.Protocol,
                status       = ticket.Status.ToWireValue(),
                department_id = ticket.DepartmentId,
                channel      = ticket.Channel.ToWireValue(),
                created_at   = ticket.CreatedAt,
            });

            if (ticket.AttendantId.HasValue)
            {
                await eventPublisher.PublishAssignedAsync(tenantSlug, ticket.DepartmentId, new
                {
                    ticket_id    = ticket.Id,
                    protocol     = ticket.Protocol,
                    attendant_id = ticket.AttendantId,
                    department_id = ticket.DepartmentId,
                    assigned_at  = ticket.AssignedAt,
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to publish WS events for ticket {TicketId}", ticket.Id);
        }
    }

    private static ContactSourceChannel MapContactChannel(TicketChannel channel) => channel switch
    {
        TicketChannel.LiveChat => ContactSourceChannel.LiveChat,
        TicketChannel.WhatsApp => ContactSourceChannel.WhatsApp,
        _                      => ContactSourceChannel.Manual,
    };

    private static string ChannelLabel(TicketChannel channel) => channel switch
    {
        TicketChannel.LiveChat => "Live Chat",
        TicketChannel.WhatsApp => "WhatsApp",
        _                      => "atendimento",
    };
}

public sealed class TicketCreationException(string errorCode, string message)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
