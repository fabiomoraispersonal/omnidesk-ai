using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Distribution;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

public class SlaCalculatorTests
{
    private static Department DeptWithSla(int? first = 30, int? resolution = 240, bool withHours = true)
    {
        var d = new Department
        {
            Id = Guid.NewGuid(),
            Name = "X",
            SlaFirstResponseMinutes = first,
            SlaResolutionMinutes = resolution,
        };
        if (withHours)
        {
            d.BusinessHoursStart = new TimeOnly(9, 0);
            d.BusinessHoursEnd = new TimeOnly(18, 0);
            d.BusinessDays = new[] { 1, 2, 3, 4, 5 };
        }
        return d;
    }

    [Fact]
    public void NotConfigured_WhenBothTargetsNull()
    {
        var dept = DeptWithSla(first: null, resolution: null);
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            DepartmentId = dept.Id,
        };
        var snap = SlaCalculator.Compute(ticket, dept, DateTimeOffset.UtcNow);
        Assert.Equal(SlaStatus.NotConfigured, snap.FirstResponseStatus);
        Assert.Equal(SlaStatus.NotConfigured, snap.ResolutionStatus);
    }

    [Theory]
    [InlineData(20, SlaStatus.Ok)]
    [InlineData(24, SlaStatus.Warning)]
    [InlineData(30, SlaStatus.Overdue)]
    public void StatusThresholds_AreRespected(int elapsed, SlaStatus expected)
    {
        var dept = DeptWithSla(first: 30, resolution: 240, withHours: false);
        var assignedAt = DateTimeOffset.UtcNow.AddMinutes(-elapsed);
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            CreatedAt = assignedAt,
            AssignedAt = assignedAt,
            SlaStartedAt = assignedAt,
            DepartmentId = dept.Id,
        };
        var snap = SlaCalculator.Compute(ticket, dept, DateTimeOffset.UtcNow);
        Assert.Equal(expected, snap.FirstResponseStatus);
    }

    [Fact]
    public void Pause_OutsideBusinessHours_DoesNotCount()
    {
        // Friday 17:50 → ticket assigned. Department closes 18:00.
        // Monday 09:30 — only 10 min Friday + 30 min Monday = 40 business minutes.
        var dept = DeptWithSla(first: 60, resolution: 240, withHours: true);
        var assignedAt = new DateTimeOffset(2026, 5, 8, 17, 50, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 11, 9, 30, 0, TimeSpan.Zero);
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(), CreatedAt = assignedAt,
            AssignedAt = assignedAt, SlaStartedAt = assignedAt,
            DepartmentId = dept.Id,
        };
        var snap = SlaCalculator.Compute(ticket, dept, now);
        Assert.Equal(40, snap.FirstResponseElapsedMinutes);
        // 40/60 = 66% → Ok (below 80%)
        Assert.Equal(SlaStatus.Ok, snap.FirstResponseStatus);
    }

    [Fact]
    public void FirstResponseTarget_NoAssignment_ReturnsNotConfigured()
    {
        var dept = DeptWithSla(first: 30, resolution: 240, withHours: false);
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AssignedAt = null,
            SlaStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DepartmentId = dept.Id,
        };
        var snap = SlaCalculator.Compute(ticket, dept, DateTimeOffset.UtcNow);
        Assert.Equal(SlaStatus.NotConfigured, snap.FirstResponseStatus);
        Assert.Equal(SlaStatus.Ok, snap.ResolutionStatus);
    }
}
