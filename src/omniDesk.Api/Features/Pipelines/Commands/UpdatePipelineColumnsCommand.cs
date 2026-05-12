using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Pipelines;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Pipelines.Commands;

public record PipelineColumnInput(
    Guid? Id,
    string Name,
    string StatusMapping,
    int Order,
    string? Color);

public record UpdatePipelineColumnsRequest(PipelineColumnInput[] Columns);

/// <summary>
/// Spec 009 US9 — T172.
/// Validates and transactionally updates 3 pipeline columns (rename, reorder, recolor).
/// Validates via PipelineStatusMapping.Validate — exactly 3 unique status_mappings required.
/// </summary>
public class UpdatePipelineColumnsCommand(AppDbContext db)
{
    private static readonly System.Text.RegularExpressions.Regex HexColor =
        new(@"^#[0-9A-Fa-f]{6}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<(bool Found, string? Error)> ExecuteAsync(
        Guid pipelineId,
        UpdatePipelineColumnsRequest req,
        CancellationToken ct)
    {
        var pipeline = await db.Pipelines
            .Include(p => p.Columns)
            .FirstOrDefaultAsync(p => p.Id == pipelineId && p.DeletedAt == null, ct);

        if (pipeline is null)
            return (false, null);

        // Build domain columns for validation
        var incoming = req.Columns.Select(c => new PipelineColumn
        {
            Name          = c.Name?.Trim() ?? string.Empty,
            StatusMapping = c.StatusMapping?.Trim().ToLower() ?? string.Empty,
            Order         = c.Order,
            Color         = c.Color,
        }).ToList();

        var (isValid, error) = PipelineStatusMapping.Validate(incoming);
        if (!isValid)
            return (true, error ?? "VALIDATION_ERROR");

        // Validate color hex format
        foreach (var col in incoming.Where(c => c.Color is not null))
        {
            if (!HexColor.IsMatch(col.Color!))
                return (true, "INVALID_COLOR_FORMAT");
        }

        // Transactional update of existing columns
        // Match existing by StatusMapping (stable key) rather than Id
        var now = DateTimeOffset.UtcNow;
        foreach (var input in req.Columns)
        {
            var existing = pipeline.Columns
                .FirstOrDefault(c => c.StatusMapping == input.StatusMapping.Trim().ToLower());

            if (existing is not null)
            {
                existing.Name      = input.Name.Trim();
                existing.Order     = input.Order;
                existing.Color     = input.Color;
                existing.UpdatedAt = now;
            }
        }

        pipeline.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return (true, null);
    }
}
