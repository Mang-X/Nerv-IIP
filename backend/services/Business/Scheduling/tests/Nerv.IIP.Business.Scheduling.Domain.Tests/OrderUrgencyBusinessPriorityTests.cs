using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Domain.Services;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

public sealed class OrderUrgencyBusinessPriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_requires_actor_and_reason()
    {
        Assert.Throws<ArgumentException>(() => OrderUrgencyBusinessPriority.Create(
            "org", "env", "WO-1", "SO-1", BusinessPriorityLevel.P1, "", "reason", Now, null));
        Assert.Throws<ArgumentException>(() => OrderUrgencyBusinessPriority.Create(
            "org", "env", "WO-1", "SO-1", BusinessPriorityLevel.P1, "planner", "", Now, null));
    }

    [Fact]
    public void Changes_increment_revision_and_append_an_immutable_audit_fact()
    {
        var priority = OrderUrgencyBusinessPriority.Create(
            "org", "env", "WO-1", "SO-1", BusinessPriorityLevel.P2,
            "planner-1", "standard commitment", Now, null);

        var change = priority.Change(
            BusinessPriorityLevel.P0,
            "manager-1",
            "customer line stopped",
            Now.AddHours(1),
            Now.AddDays(1));

        Assert.Equal(2, priority.Revision);
        Assert.Equal(BusinessPriorityLevel.P0, priority.Level);
        Assert.Equal("manager-1", priority.SetBy);
        Assert.Equal(2, change.Revision);
        Assert.Equal(BusinessPriorityLevel.P2, change.PreviousLevel);
        Assert.Equal(BusinessPriorityLevel.P0, change.NewLevel);
        Assert.Equal("customer line stopped", change.Reason);
    }

    [Fact]
    public void Expired_priority_is_retained_for_audit_but_not_effective()
    {
        var priority = OrderUrgencyBusinessPriority.Create(
            "org", "env", "WO-1", "SO-1", BusinessPriorityLevel.P1,
            "planner-1", "contract risk", Now, Now.AddHours(1));

        Assert.True(priority.IsEffectiveAt(Now.AddMinutes(59)));
        Assert.False(priority.IsEffectiveAt(Now.AddHours(1)));
        Assert.Equal(BusinessPriorityLevel.P1, priority.Level);
    }
}
