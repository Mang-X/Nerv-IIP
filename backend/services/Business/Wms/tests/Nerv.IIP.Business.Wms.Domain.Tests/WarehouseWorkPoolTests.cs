using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class WarehouseWorkPoolTests
{
    [Fact]
    public void Work_pool_and_membership_preserve_site_and_effective_assignment_facts()
    {
        var effectiveFrom = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var effectiveTo = effectiveFrom.AddYears(1);
        var pool = WarehouseWorkPool.Create(
            "org-001",
            "env-dev",
            "POOL-RECEIVING",
            "收货上架组",
            "SITE-001");
        var membership = WarehouseWorkPoolMembership.Create(
            "org-001",
            "env-dev",
            pool.PoolCode,
            "user-emp-049",
            effectiveFrom,
            effectiveTo);

        Assert.Equal("SITE-001", pool.SiteCode);
        Assert.True(pool.Active);
        Assert.True(membership.IsEffectiveAt(effectiveFrom.AddDays(1)));
        Assert.False(membership.IsEffectiveAt(effectiveFrom.AddSeconds(-1)));
        Assert.False(membership.IsEffectiveAt(effectiveTo));

        membership.Deactivate(effectiveFrom.AddDays(2));

        Assert.False(membership.IsEffectiveAt(effectiveFrom.AddDays(3)));
    }

    [Fact]
    public void Membership_rejects_an_empty_or_inverted_effective_window()
    {
        var from = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => WarehouseWorkPoolMembership.Create(
            "org-001",
            "env-dev",
            " ",
            "user-emp-049",
            from,
            from.AddDays(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarehouseWorkPoolMembership.Create(
            "org-001",
            "env-dev",
            "POOL-RECEIVING",
            "user-emp-049",
            from,
            from));
    }

    [Fact]
    public void Manual_claim_and_wcs_claim_are_mutually_exclusive()
    {
        var manualTask = CreatePickingTask("TASK-MANUAL");
        manualTask.ClaimManualExecution("user-emp-049", 1);

        Assert.Equal(WarehouseTaskExecutionChannel.Manual, manualTask.ExecutionChannel);
        Assert.Equal("user-emp-049", manualTask.ExecutionClaimedBy);
        Assert.Throws<InvalidOperationException>(() =>
            manualTask.ClaimWcsExecution("wcs-task-001", manualTask.Version));

        var wcsTask = CreatePickingTask("TASK-WCS");
        wcsTask.ClaimWcsExecution("wcs-task-002", 1);

        Assert.Equal(WarehouseTaskExecutionChannel.Wcs, wcsTask.ExecutionChannel);
        Assert.Equal("wcs-task-002", wcsTask.ExecutionClaimedBy);
        Assert.Equal(WarehouseTaskStatus.InProgress, wcsTask.Status);
        Assert.NotNull(wcsTask.StartedAtUtc);
        Assert.Throws<InvalidOperationException>(() =>
            wcsTask.ClaimManualExecution("user-emp-049", wcsTask.Version));
    }

    [Fact]
    public void Pool_claim_persists_the_operator_snapshot_before_manual_start()
    {
        var task = CreatePickingTask("TASK-POOL", assignedOperatorUserId: null);

        task.Start("user-emp-049", 1, claimPoolAssignment: true);

        Assert.Equal("POOL-SHIPPING", task.AssignedPoolCode);
        Assert.Equal("user-emp-049", task.AssignedOperatorUserId);
        Assert.Equal(WarehouseTaskExecutionChannel.Manual, task.ExecutionChannel);
        Assert.Equal(WarehouseTaskStatus.InProgress, task.Status);
        Assert.Equal(2, task.Version);
    }

    [Fact]
    public void Unassigned_legacy_task_cannot_be_claimed_by_manual_or_wcs_execution()
    {
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            "TASK-LEGACY",
            "OUT-001",
            "LINE-001",
            "SKU-001",
            "pcs",
            "SITE-001",
            "BIN-001",
            "PACK-001",
            10m);

        Assert.Throws<InvalidOperationException>(() =>
            task.Start("user-emp-049", 1, claimPoolAssignment: true));
        Assert.Throws<InvalidOperationException>(() =>
            task.ClaimWcsExecution("wcs-task-legacy", 1));
        Assert.Equal(WarehouseTaskStatus.Open, task.Status);
        Assert.Equal(WarehouseTaskExecutionChannel.Unclaimed, task.ExecutionChannel);
    }

    [Fact]
    public void Wcs_progress_never_claims_an_unclaimed_task_from_a_callback()
    {
        var task = CreatePickingTask("TASK-WCS-CALLBACK", assignedOperatorUserId: null);

        Assert.Throws<InvalidOperationException>(() =>
            task.RecordWcsProgress(2m, "wcs-task-001"));
        Assert.Equal(0m, task.ExecutedQuantity);
        Assert.Equal(WarehouseTaskExecutionChannel.Unclaimed, task.ExecutionChannel);
    }

    private static WarehouseTask CreatePickingTask(
        string taskNo,
        string? assignedOperatorUserId = "user-emp-049") =>
        WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            taskNo,
            "OUT-001",
            "LINE-001",
            "SKU-001",
            "pcs",
            "SITE-001",
            "BIN-001",
            "PACK-001",
            10m,
            assignedOperatorUserId: assignedOperatorUserId,
            assignedPoolCode: "POOL-SHIPPING");
}
