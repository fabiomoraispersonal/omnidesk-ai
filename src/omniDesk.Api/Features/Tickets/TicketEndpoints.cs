using FluentValidation.Results;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets.Commands;
using omniDesk.Api.Features.Tickets.Notes;
using omniDesk.Api.Features.Tickets.Queries;
using omniDesk.Api.Features.Tickets.Validators;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Tickets;

/// <summary>
/// Spec 009 US2 — CRM ticket endpoints.
/// GET list · GET detail · PUT update · PATCH status · POST resolve · POST cancel.
/// Notes sub-resource mapped separately via TicketNotesEndpoints.
/// </summary>
public static class TicketEndpoints
{
    public static RouteGroupBuilder MapTicketEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateManualAsync);                          // T136
        group.MapGet("/{id:guid}", DetailAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPatch("/{id:guid}/status", ChangeStatusAsync);
        group.MapPost("/{id:guid}/resolve", ResolveAsync);
        group.MapPost("/{id:guid}/cancel", CancelAsync);
        group.MapPost("/{id:guid}/transfer", TransferAsync);          // T127
        group.MapPatch("/{id:guid}/attendant", ReassignAttendantAsync); // T128

        // Notes sub-resource
        group.MapTicketNotesEndpoints();

        return group;
    }

    // POST /api/tickets — T136
    private static async Task<IResult> CreateManualAsync(
        CreateManualTicketRequest req,
        ICurrentUser caller,
        CreateManualTicketCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var validator = new CreateManualTicketRequestValidator();
        var vr = validator.Validate(req);
        if (!vr.IsValid)
            return Results.Json(ValidationError(vr), statusCode: 400);

        var (data, error) = await command.ExecuteAsync(req, userId, ct);

        if (error == "DEPARTMENT_NOT_FOUND")
            return Results.Json(Error(error, "Department not found."), statusCode: 404);
        if (error is not null)
            return Results.Json(Error(error, "Failed to create ticket."), statusCode: 400);

        return Results.Created($"/api/tickets/{(data as dynamic)?.ticket_id}", new { success = true, data });
    }

    // GET /api/tickets
    private static async Task<IResult> ListAsync(
        HttpContext http,
        ICurrentUser caller,
        ListTicketsQuery query,
        CancellationToken ct,
        int page = 1,
        int per_page = 20,
        string sort = "created_at",
        string order = "desc",
        Guid? department_id = null,
        string? attendant_id = null,
        string? channel = null,
        string? priority = null,
        string? status = null,
        bool include_terminal = false,
        [Microsoft.AspNetCore.Mvc.FromQuery(Name = "tag")] string[]? tag = null,
        DateTimeOffset? created_from = null,
        DateTimeOffset? created_to = null,
        string? period = null,
        string? q = null)
    {
        var req = new ListTicketsRequest(
            Page: page, PerPage: per_page,
            Sort: sort, Order: order,
            DepartmentId: department_id,
            AttendantId: attendant_id,
            Channel: channel,
            Priority: priority,
            Status: status,
            IncludeTerminal: include_terminal,
            Tag: tag,
            CreatedFrom: created_from,
            CreatedTo: created_to,
            Period: period,
            Q: q);

        try
        {
            var (items, total) = await query.ExecuteAsync(req, caller, ct);
            return Results.Ok(new
            {
                success = true,
                data    = items,
                meta    = new { page, per_page, total },
            });
        }
        catch (ForbiddenException ex)
        {
            return Results.Json(Error(ex.Code, ex.Message), statusCode: 403);
        }
    }

    // GET /api/tickets/{id}
    private static async Task<IResult> DetailAsync(
        Guid id,
        ICurrentUser caller,
        GetTicketDetailQuery query,
        CancellationToken ct)
    {
        try
        {
            var data = await query.ExecuteAsync(id, caller, ct);
            if (data is null)
                return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);

            return Results.Ok(new { success = true, data });
        }
        catch (ForbiddenException ex)
        {
            return Results.Json(Error(ex.Code, ex.Message), statusCode: 403);
        }
    }

    // PUT /api/tickets/{id}
    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateTicketRequest req,
        ICurrentUser caller,
        UpdateTicketCommand command,
        GetTicketDetailQuery detailQuery,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var (found, _, alreadyClosed) = await command.ExecuteAsync(id, req, userId, ct);

        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);
        if (alreadyClosed)
            return Results.Json(Error("TICKET_ALREADY_CLOSED", "Cannot edit a closed ticket."), statusCode: 409);

        var data = await detailQuery.ExecuteAsync(id, caller, ct);
        return Results.Ok(new { success = true, data });
    }

    // PATCH /api/tickets/{id}/status
    private static async Task<IResult> ChangeStatusAsync(
        Guid id,
        ChangeStatusRequest req,
        ICurrentUser caller,
        ChangeTicketStatusCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var validator = new ChangeStatusValidator();
        var vr = validator.Validate(req);
        if (!vr.IsValid)
            return Results.Json(ValidationError(vr), statusCode: 400);

        var canAccess = caller.Role is Roles.TenantAdmin or Roles.Supervisor
            || caller.Role == Roles.Attendant; // detailed dept check is inside the command if needed

        var (found, forbidden, error, data) = await command.ExecuteAsync(
            id, req.Status, req.Reason, userId, canAccess, ct);

        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);
        if (forbidden)
            return Results.Json(Error("FORBIDDEN_DEPARTMENT", "Access denied."), statusCode: 403);
        if (error == "TICKET_ALREADY_CLOSED")
            return Results.Json(Error(error, "Ticket is already closed."), statusCode: 409);
        if (error == "INVALID_STATUS_TRANSITION")
            return Results.Json(Error(error, "Status transition not allowed."), statusCode: 400);
        if (error is not null)
            return Results.Json(Error(error, "Invalid request."), statusCode: 400);

        return Results.Ok(new { success = true, data });
    }

    // POST /api/tickets/{id}/resolve
    private static async Task<IResult> ResolveAsync(
        Guid id,
        ResolveRequest req,
        ICurrentUser caller,
        ResolveTicketCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var (found, alreadyClosed, _) = await command.ExecuteAsync(id, req.ResolutionNote, userId, ct);

        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);
        if (alreadyClosed)
            return Results.Json(Error("TICKET_ALREADY_CLOSED", "Ticket is already closed."), statusCode: 409);

        return Results.Ok(new { success = true });
    }

    // POST /api/tickets/{id}/cancel
    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelRequest req,
        ICurrentUser caller,
        CancelTicketCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var (found, alreadyClosed) = await command.ExecuteAsync(id, req.Reason, userId, ct);

        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);
        if (alreadyClosed)
            return Results.Json(Error("TICKET_ALREADY_CLOSED", "Ticket is already closed."), statusCode: 409);

        return Results.Ok(new { success = true });
    }

    // POST /api/tickets/{id}/transfer — T127
    private static async Task<IResult> TransferAsync(
        Guid id,
        TransferTicketRequest req,
        ICurrentUser caller,
        TransferTicketCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var validator = new TransferTicketRequestValidator();
        var vr = validator.Validate(req);
        if (!vr.IsValid)
            return Results.Json(ValidationError(vr), statusCode: 400);

        var (found, forbidden, error, data) = await command.ExecuteAsync(id, req, userId, ct);

        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);
        if (forbidden)
            return Results.Json(Error("FORBIDDEN_DEPARTMENT", "Access denied."), statusCode: 403);
        if (error == "TICKET_ALREADY_CLOSED")
            return Results.Json(Error(error, "Ticket is already closed."), statusCode: 409);
        if (error == "INVALID_TRANSFER_TARGET")
            return Results.Json(Error(error, "Invalid transfer target."), statusCode: 400);
        if (error == "TARGET_NOT_FOUND")
            return Results.Json(Error(error, "Transfer target not found."), statusCode: 404);
        if (error is not null)
            return Results.Json(Error(error, "Transfer failed."), statusCode: 400);

        return Results.Ok(new { success = true, data });
    }

    // PATCH /api/tickets/{id}/attendant — T128 (quick reassign)
    private static async Task<IResult> ReassignAttendantAsync(
        Guid id,
        ReassignAttendantRequest req,
        ICurrentUser caller,
        TransferTicketCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated || caller.UserId is not Guid userId)
            return Results.Unauthorized();

        var transfer = new TransferTicketRequest(
            TargetType: "attendant",
            TargetAttendantId: req.AttendantId,
            TargetDepartmentId: null,
            Note: null);

        var (found, forbidden, error, data) = await command.ExecuteAsync(id, transfer, userId, ct);

        if (!found)
            return Results.Json(Error("TICKET_NOT_FOUND", "Ticket not found."), statusCode: 404);
        if (forbidden)
            return Results.Json(Error("FORBIDDEN_DEPARTMENT", "Access denied."), statusCode: 403);
        if (error == "TICKET_ALREADY_CLOSED")
            return Results.Json(Error(error, "Ticket is already closed."), statusCode: 409);
        if (error == "TARGET_NOT_FOUND")
            return Results.Json(Error(error, "Attendant not found."), statusCode: 404);
        if (error is not null)
            return Results.Json(Error(error, "Reassignment failed."), statusCode: 400);

        return Results.Ok(new { success = true, data });
    }

    private static object Error(string code, string message) =>
        new { success = false, error = new { code, message } };

    private static object ValidationError(ValidationResult vr) =>
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

public record ResolveRequest(string? ResolutionNote);
public record CancelRequest(string? Reason);
public record ReassignAttendantRequest(Guid? AttendantId);
