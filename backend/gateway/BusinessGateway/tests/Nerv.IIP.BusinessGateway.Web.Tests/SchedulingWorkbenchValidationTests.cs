using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class SchedulingWorkbenchValidationTests
{
    [Fact]
    public void Gateway_validators_use_the_contract_limit_and_reject_duplicate_ids()
    {
        var start = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var overLimit = Enumerable.Range(1, SchedulingWorkbenchLimits.MaxOrderCount + 1)
            .Select(index => new BusinessConsoleSchedulingWorkbenchOrderSelection($"WO-{index}", 10, false))
            .ToArray();
        var createResult = new BusinessConsoleCreateSchedulingWorkbenchPlanRequestValidator().Validate(
            new BusinessConsoleCreateSchedulingWorkbenchPlanRequest(
                "org-001",
                "env-dev",
                start,
                start.AddDays(1),
                overLimit));
        var revisionResult = new BusinessConsoleCreateSchedulePlanRevisionRequestValidator().Validate(
            new BusinessConsoleCreateSchedulePlanRevisionRequest(
                "plan-001",
                "org-001",
                "env-dev",
                ["WO-001", "WO-001"],
                []));
        var manyLocks = Enumerable.Range(1, SchedulingWorkbenchLimits.MaxOrderCount + 1)
            .Select(index => new SchedulingLockedAssignmentContract(
                $"assignment-{index}",
                $"WO-{index}",
                "OP-10",
                10,
                "RES-1",
                "WC-1",
                start,
                start.AddHours(1),
                "ui"))
            .ToArray();
        var manyLocksResult = new BusinessConsoleCreateSchedulePlanRevisionRequestValidator().Validate(
            new BusinessConsoleCreateSchedulePlanRevisionRequest(
                "plan-001",
                "org-001",
                "env-dev",
                ["WO-001"],
                manyLocks));

        Assert.False(createResult.IsValid);
        Assert.False(revisionResult.IsValid);
        Assert.True(manyLocksResult.IsValid);
    }
}
