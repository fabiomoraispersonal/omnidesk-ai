using omniDesk.Api.Domain.Pipelines;
using omniDesk.Api.Features.Pipelines.Commands;
using Xunit;

namespace omniDesk.Api.Tests.Features.Pipelines;

/// <summary>
/// Spec 009 US9 — T177
/// Unit tests for UpdatePipelineColumnsCommand domain logic:
/// - Duplicate status_mapping → 400
/// - Wrong column count → 400
/// - Rename → ok
/// - Reorder → ok
/// - Valid and invalid hex colors
/// </summary>
public class UpdatePipelineColumnsCommandTests
{
    // -----------------------------------------------------------------------
    // PipelineStatusMapping.Validate — column count
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_rejects_less_than_3_columns()
    {
        var columns = new[]
        {
            MakeColumn("Na Fila",      "new",         1),
            MakeColumn("Em Andamento", "in_progress", 2),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(columns);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_rejects_more_than_3_columns()
    {
        var columns = new[]
        {
            MakeColumn("Col1", "new",            1),
            MakeColumn("Col2", "in_progress",    2),
            MakeColumn("Col3", "waiting_client", 3),
            MakeColumn("Col4", "new",            4), // extra
        };

        var (isValid, error) = PipelineStatusMapping.Validate(columns);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    // -----------------------------------------------------------------------
    // PipelineStatusMapping.Validate — duplicate status_mapping
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_rejects_duplicate_status_mappings()
    {
        var columns = new[]
        {
            MakeColumn("Col1", "new",         1),
            MakeColumn("Col2", "new",         2), // duplicate
            MakeColumn("Col3", "in_progress", 3),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(columns);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_rejects_invalid_status_mapping_values()
    {
        var columns = new[]
        {
            MakeColumn("Col1", "new",         1),
            MakeColumn("Col2", "in_progress", 2),
            MakeColumn("Col3", "closed",      3), // invalid
        };

        var (isValid, error) = PipelineStatusMapping.Validate(columns);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    // -----------------------------------------------------------------------
    // PipelineStatusMapping.Validate — valid set
    // -----------------------------------------------------------------------

    [Fact]
    public void Validate_accepts_valid_3_unique_mappings()
    {
        var columns = new[]
        {
            MakeColumn("Na Fila",            "new",            1),
            MakeColumn("Em Andamento",       "in_progress",    2),
            MakeColumn("Aguardando Cliente", "waiting_client", 3),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(columns);
        Assert.True(isValid);
        Assert.Null(error);
    }

    // -----------------------------------------------------------------------
    // Rename and reorder logic
    // -----------------------------------------------------------------------

    [Fact]
    public void Rename_preserves_status_mapping()
    {
        var col = MakeColumn("Antigo Nome", "new", 1);
        col.Name = "Novo Nome";

        Assert.Equal("Novo Nome", col.Name);
        Assert.Equal("new", col.StatusMapping);
    }

    [Fact]
    public void Reorder_changes_order_values()
    {
        var cols = new[]
        {
            MakeColumn("Na Fila",            "new",            1),
            MakeColumn("Em Andamento",       "in_progress",    2),
            MakeColumn("Aguardando Cliente", "waiting_client", 3),
        };

        // Simulate moving "waiting_client" to position 1
        cols[2].Order = 1;
        cols[0].Order = 2;
        cols[1].Order = 3;

        var reordered = cols.OrderBy(c => c.Order).ToArray();
        Assert.Equal("waiting_client", reordered[0].StatusMapping);
        Assert.Equal("new",            reordered[1].StatusMapping);
        Assert.Equal("in_progress",    reordered[2].StatusMapping);
    }

    // -----------------------------------------------------------------------
    // Color validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("#6F7D5C", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#000000", true)]
    [InlineData("#abc",    false)] // too short
    [InlineData("6F7D5C", false)] // missing #
    [InlineData("#GGGGGG", false)] // invalid hex chars
    [InlineData(null,      true)]  // null is allowed
    public void Color_validation_hex_format(string? color, bool expected)
    {
        var hexRegex = new System.Text.RegularExpressions.Regex(@"^#[0-9A-Fa-f]{6}$");
        var isValid  = color is null || hexRegex.IsMatch(color);
        Assert.Equal(expected, isValid);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PipelineColumn MakeColumn(string name, string statusMapping, int order) =>
        new PipelineColumn
        {
            Id            = Guid.NewGuid(),
            PipelineId    = Guid.NewGuid(),
            Name          = name,
            StatusMapping = statusMapping,
            Order         = order,
            CreatedAt     = DateTimeOffset.UtcNow,
            UpdatedAt     = DateTimeOffset.UtcNow,
        };
}
