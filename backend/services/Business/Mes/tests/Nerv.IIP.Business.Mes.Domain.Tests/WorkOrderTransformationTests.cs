using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class WorkOrderTransformationTests
{
    [Fact]
    public void Split_preserves_quantity_uom_and_parent_child_lineage()
    {
        var parent = Snapshot("WO-PARENT", 10m);
        var children = new[]
        {
            Snapshot("WO-CHILD-1", 4m),
            Snapshot("WO-CHILD-2", 6m),
        };

        var transformation = WorkOrderTransformation.CreateSplit(
            "org-001",
            "env-dev",
            parent,
            children,
            "split-request-001",
            "fingerprint-001",
            "user:planner-001",
            "按生产批次拆分",
            At(1));

        Assert.Equal(WorkOrderTransformationType.Split, transformation.Type);
        Assert.Equal(WorkOrderTransformationStatus.Applied, transformation.Status);
        Assert.Equal("split-request-001", transformation.IdempotencyKey);
        Assert.Equal("fingerprint-001", transformation.RequestFingerprint);
        Assert.Equal(10m, transformation.Lines.Sum(x => x.Quantity));
        Assert.Equal(2, transformation.Lines.Count);
        Assert.All(transformation.Lines, line =>
        {
            Assert.Equal(WorkOrderLineageType.Split, line.LineageType);
            Assert.Equal(parent.WorkOrderId, line.SourceWorkOrderId);
            Assert.Equal(parent.UomCode, line.UomCode);
            Assert.Equal(parent.Version, line.SourceVersion);
            Assert.Equal(WorkOrder.CreatedStatus, line.SourceStatus);
        });
        Assert.Equal(
            children.Select(x => x.WorkOrderId).OrderBy(x => x),
            transformation.Lines.Select(x => x.TargetWorkOrderId).OrderBy(x => x));
    }

    [Fact]
    public void Merge_requires_same_sku_and_uom_and_conserves_quantity()
    {
        var sources = new[]
        {
            Snapshot("WO-SOURCE-1", 3m),
            Snapshot("WO-SOURCE-2", 7m),
        };
        var target = Snapshot("WO-MERGED", 10m);

        var transformation = WorkOrderTransformation.CreateMerge(
            "org-001",
            "env-dev",
            sources,
            target,
            "merge-request-001",
            "fingerprint-002",
            "user:planner-001",
            "合并同 SKU 小单",
            At(2));

        Assert.Equal(WorkOrderTransformationType.Merge, transformation.Type);
        Assert.Equal(10m, transformation.Lines.Sum(x => x.Quantity));
        Assert.All(transformation.Lines, line =>
        {
            Assert.Equal(WorkOrderLineageType.Merge, line.LineageType);
            Assert.Equal(target.WorkOrderId, line.TargetWorkOrderId);
            Assert.Equal(target.UomCode, line.UomCode);
            Assert.Equal(target.Version, line.TargetVersion);
        });
    }

    [Fact]
    public void Merge_rejects_different_sku_or_uom()
    {
        Assert.Throws<InvalidOperationException>(() => WorkOrderTransformation.CreateMerge(
            "org-001",
            "env-dev",
            [Snapshot("WO-SOURCE-1", 3m), Snapshot("WO-SOURCE-2", 7m, skuId: "SKU-002")],
            Snapshot("WO-MERGED", 10m),
            "merge-request-sku-mismatch",
            "fingerprint-sku-mismatch",
            "user:planner-001",
            "禁止跨 SKU 合并",
            At(2)));

        Assert.Throws<InvalidOperationException>(() => WorkOrderTransformation.CreateMerge(
            "org-001",
            "env-dev",
            [Snapshot("WO-SOURCE-1", 3m), Snapshot("WO-SOURCE-2", 7m, uomCode: "KG")],
            Snapshot("WO-MERGED", 10m),
            "merge-request-uom-mismatch",
            "fingerprint-uom-mismatch",
            "user:planner-001",
            "禁止跨 UOM 合并",
            At(2)));
    }

    [Fact]
    public void Split_rejects_quantity_drift_and_uom_mismatch()
    {
        var parent = Snapshot("WO-PARENT", 10m);

        Assert.Throws<InvalidOperationException>(() => WorkOrderTransformation.CreateSplit(
            "org-001",
            "env-dev",
            parent,
            [Snapshot("WO-CHILD-1", 4m), Snapshot("WO-CHILD-2", 5m)],
            "split-request-drift",
            "fingerprint-drift",
            "user:planner-001",
            "数量不守恒",
            At(3)));

        Assert.Throws<InvalidOperationException>(() => WorkOrderTransformation.CreateSplit(
            "org-001",
            "env-dev",
            parent,
            [Snapshot("WO-CHILD-1", 5m, uomCode: "KG"), Snapshot("WO-CHILD-2", 5m, uomCode: "KG")],
            "split-request-uom",
            "fingerprint-uom",
            "user:planner-001",
            "单位不一致",
            At(4)));
    }

    [Theory]
    [InlineData(WorkOrder.StartedStatus)]
    [InlineData(WorkOrder.HoldStatus)]
    [InlineData(WorkOrder.CompletedStatus)]
    [InlineData(WorkOrder.ClosedStatus)]
    [InlineData(WorkOrder.CancelledStatus)]
    [InlineData(WorkOrder.ScrappedStatus)]
    public void Split_rejects_non_transformable_source_status(string status)
    {
        var source = Snapshot("WO-SOURCE", 10m, status: status);

        Assert.Throws<InvalidOperationException>(() => WorkOrderTransformation.CreateSplit(
            "org-001",
            "env-dev",
            source,
            [Snapshot("WO-CHILD-1", 5m), Snapshot("WO-CHILD-2", 5m)],
            $"split-request-{status}",
            $"fingerprint-{status}",
            "user:planner-001",
            "状态矩阵",
            At(5)));
    }

    [Fact]
    public void Idempotency_key_replay_requires_the_same_request_fingerprint()
    {
        var transformation = WorkOrderTransformation.CreateSplit(
            "org-001",
            "env-dev",
            Snapshot("WO-PARENT", 10m),
            [Snapshot("WO-CHILD-1", 5m), Snapshot("WO-CHILD-2", 5m)],
            "split-request-replay",
            "fingerprint-replay",
            "user:planner-001",
            "幂等重试",
            At(6));

        transformation.EnsureReplayMatches("fingerprint-replay");
        Assert.Throws<InvalidOperationException>(() => transformation.EnsureReplayMatches("fingerprint-different"));
    }

    [Fact]
    public void Work_order_version_advances_and_transformed_status_blocks_execution()
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-VERSION-001",
            "SKU-001",
            "PV-001",
            10m,
            10,
            At(7),
            "PCS");

        Assert.Equal(1, workOrder.Version);
        workOrder.MarkReleased();
        Assert.Equal(2, workOrder.Version);
        workOrder.MarkSplit();
        Assert.Equal(WorkOrder.SplitStatus, workOrder.Status);
        Assert.Equal(3, workOrder.Version);
        Assert.Throws<InvalidOperationException>(() => workOrder.RecordProductionProgress(1m, 0m, At(8)));
    }

    private static WorkOrderTransformationWorkOrderSnapshot Snapshot(
        string workOrderId,
        decimal quantity,
        string skuId = "SKU-001",
        string uomCode = "PCS",
        string status = WorkOrder.CreatedStatus) =>
        new(
            workOrderId,
            skuId,
            "PV-001",
            uomCode,
            quantity,
            status,
            1);

    private static DateTimeOffset At(int hour) =>
        DateTimeOffset.Parse("2026-08-25T00:00:00Z").AddHours(hour);
}
