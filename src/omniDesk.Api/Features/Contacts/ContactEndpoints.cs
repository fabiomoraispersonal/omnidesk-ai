using omniDesk.Api.Features.Contacts.Commands;
using omniDesk.Api.Features.Contacts.Queries;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Contacts;

/// <summary>
/// Spec 009 US6 — T147.
/// GET /api/contacts            — list (search)
/// GET /api/contacts/{id}       — detail
/// POST /api/contacts           — create (find-or-create via dedup)
/// PUT /api/contacts/{id}       — update fields
/// GET /api/contacts/{id}/tickets       — paginated ticket history
/// GET /api/contacts/{id}/conversations — paginated conversation history
/// </summary>
public static class ContactEndpoints
{
    public static RouteGroupBuilder MapContactEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", DetailAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapGet("/{id:guid}/tickets", ListTicketsAsync);
        group.MapGet("/{id:guid}/conversations", ListConversationsAsync);

        return group;
    }

    // GET /api/contacts
    private static async Task<IResult> ListAsync(
        ICurrentUser caller,
        ListContactsQuery query,
        CancellationToken ct,
        int page = 1,
        int per_page = 20,
        string? q = null)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var req = new ListContactsRequest(page, per_page, q);
        var (items, total) = await query.ExecuteAsync(req, ct);

        return Results.Ok(new
        {
            success = true,
            data    = items,
            meta    = new { page, per_page, total },
        });
    }

    // GET /api/contacts/{id}
    private static async Task<IResult> DetailAsync(
        Guid id,
        ICurrentUser caller,
        GetContactQuery query,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var data = await query.ExecuteAsync(id, ct);
        if (data is null)
            return Results.Json(Error("CONTACT_NOT_FOUND", "Contact not found."), statusCode: 404);

        return Results.Ok(new { success = true, data });
    }

    // POST /api/contacts
    private static async Task<IResult> CreateAsync(
        CreateContactRequest req,
        ICurrentUser caller,
        CreateContactCommand command,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var (data, error) = await command.ExecuteAsync(req, ct);

        if (error == "CONTACT_NO_IDENTIFIER")
            return Results.Json(
                Error(error, "At least one of name, email, or phone is required."),
                statusCode: 400);

        return Results.Created($"/api/contacts/{(data as dynamic)?.id}", new { success = true, data });
    }

    // PUT /api/contacts/{id}
    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateContactRequest req,
        ICurrentUser caller,
        UpdateContactCommand command,
        GetContactQuery detailQuery,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var (found, error) = await command.ExecuteAsync(id, req, ct);

        if (!found)
            return Results.Json(Error("CONTACT_NOT_FOUND", "Contact not found."), statusCode: 404);
        if (error == "EMAIL_CONFLICT")
            return Results.Json(Error(error, "A contact with this email already exists."), statusCode: 409);
        if (error == "PHONE_CONFLICT")
            return Results.Json(Error(error, "A contact with this phone already exists."), statusCode: 409);

        var data = await detailQuery.ExecuteAsync(id, ct);
        return Results.Ok(new { success = true, data });
    }

    // GET /api/contacts/{id}/tickets
    private static async Task<IResult> ListTicketsAsync(
        Guid id,
        ICurrentUser caller,
        ListContactTicketsQuery query,
        CancellationToken ct,
        int page = 1,
        int per_page = 20)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var (items, total) = await query.ExecuteAsync(id, page, per_page, ct);
        return Results.Ok(new
        {
            success = true,
            data    = items,
            meta    = new { page, per_page, total },
        });
    }

    // GET /api/contacts/{id}/conversations
    private static async Task<IResult> ListConversationsAsync(
        Guid id,
        ICurrentUser caller,
        ListContactConversationsQuery query,
        CancellationToken ct,
        int page = 1,
        int per_page = 20)
    {
        if (!caller.IsAuthenticated)
            return Results.Unauthorized();

        var (items, total) = await query.ExecuteAsync(id, page, per_page, ct);
        return Results.Ok(new
        {
            success = true,
            data    = items,
            meta    = new { page, per_page, total },
        });
    }

    private static object Error(string code, string message) =>
        new { success = false, error = new { code, message } };
}
