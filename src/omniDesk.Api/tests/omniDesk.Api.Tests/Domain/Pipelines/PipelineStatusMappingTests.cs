using omniDesk.Api.Domain.Pipelines;
using Xunit;

namespace omniDesk.Api.Tests.Domain.Pipelines;

/// <summary>
/// Spec 009 — pipeline column validation rules (PipelineStatusMapping.Validate).
///
/// Rules:
///   - Exactly 3 columns are required.
///   - Each column's StatusMapping must be one of: "new", "in_progress", "waiting_client".
///   - All three status values must be unique (no duplicates).
/// </summary>
public class PipelineStatusMappingTests
{
    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static PipelineColumn Col(string statusMapping, string name = "Column", int order = 0) =>
        new PipelineColumn
        {
            Id            = Guid.NewGuid(),
            PipelineId    = Guid.NewGuid(),
            Name          = name,
            StatusMapping = statusMapping,
            Order         = order,
        };

    private static IReadOnlyList<PipelineColumn> ValidColumns() =>
    [
        Col("new",            "New",             0),
        Col("in_progress",    "In Progress",     1),
        Col("waiting_client", "Waiting Client",  2),
    ];

    // ------------------------------------------------------------------ //
    // Valid configurations
    // ------------------------------------------------------------------ //

    [Fact]
    public void Validate_exactly_3_unique_valid_mappings_is_valid()
    {
        var (isValid, error) = PipelineStatusMapping.Validate(ValidColumns());

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_different_ordering_of_3_valid_statuses_is_valid()
    {
        // Reorder: waiting_client first, then in_progress, then new
        var cols = new[]
        {
            Col("waiting_client", "Waiting", 0),
            Col("in_progress",    "Working", 1),
            Col("new",            "Inbox",   2),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void Validate_another_valid_ordering_is_accepted()
    {
        var cols = new[]
        {
            Col("in_progress",    "In Progress",    0),
            Col("new",            "New",            1),
            Col("waiting_client", "Waiting Client", 2),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.True(isValid);
        Assert.Null(error);
    }

    // ------------------------------------------------------------------ //
    // Invalid: wrong column count
    // ------------------------------------------------------------------ //

    [Fact]
    public void Validate_2_columns_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("in_progress"),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("3", error); // message must mention 3
    }

    [Fact]
    public void Validate_4_columns_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("in_progress"),
            Col("waiting_client"),
            Col("new"), // extra
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("3", error);
    }

    [Fact]
    public void Validate_0_columns_is_rejected()
    {
        var cols = Array.Empty<PipelineColumn>();

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_1_column_is_rejected()
    {
        var cols = new[] { Col("new") };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    // ------------------------------------------------------------------ //
    // Invalid: duplicate status mappings
    // ------------------------------------------------------------------ //

    [Fact]
    public void Validate_two_columns_with_same_new_mapping_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("new"),         // duplicate
            Col("in_progress"),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_two_columns_with_same_in_progress_mapping_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("in_progress"),
            Col("in_progress"), // duplicate
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_all_three_columns_with_same_mapping_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("new"),
            Col("new"),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    // ------------------------------------------------------------------ //
    // Invalid: unknown status values
    // ------------------------------------------------------------------ //

    [Fact]
    public void Validate_unknown_status_value_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("in_progress"),
            Col("resolved"),    // not an allowed column mapping
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_empty_string_mapping_is_rejected()
    {
        var cols = new[]
        {
            Col("new"),
            Col("in_progress"),
            Col(""),            // empty is not a valid mapping
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_uppercase_mapping_is_rejected()
    {
        // Mapping comparison is case-sensitive; "New" is not "new"
        var cols = new[]
        {
            Col("New"),
            Col("in_progress"),
            Col("waiting_client"),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Validate_cancelled_mapping_is_rejected()
    {
        // "cancelled" is a valid TicketStatus wire value but NOT a valid pipeline column mapping
        var cols = new[]
        {
            Col("new"),
            Col("in_progress"),
            Col("cancelled"),
        };

        var (isValid, error) = PipelineStatusMapping.Validate(cols);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    // ------------------------------------------------------------------ //
    // Error message quality
    // ------------------------------------------------------------------ //

    [Fact]
    public void Validate_wrong_count_error_message_includes_actual_count()
    {
        var cols = new[] { Col("new"), Col("in_progress") };

        var (_, error) = PipelineStatusMapping.Validate(cols);

        Assert.NotNull(error);
        Assert.Contains("2", error!); // actual count reported
    }
}
