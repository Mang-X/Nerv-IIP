using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderTransformationPostgresTests
{
    [MesRealPostgresFact]
    public async Task Split_lineage_audit_and_work_order_version_survive_postgres_scope_recreation()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-25T10:00:00Z");

        await using (var setup = CreateContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync(CancellationToken.None);

            var parent = WorkOrder.Create(
                "org-001", "env-dev", "WO-PARENT-001", "SKU-001", "PV-001", 10m, 10,
                occurredAtUtc.AddHours(4), "PCS");
            var child1 = WorkOrder.Create(
                "org-001", "env-dev", "WO-CHILD-001", "SKU-001", "PV-001", 4m, 10,
                occurredAtUtc.AddHours(4), "PCS");
            var child2 = WorkOrder.Create(
                "org-001", "env-dev", "WO-CHILD-002", "SKU-001", "PV-001", 6m, 10,
                occurredAtUtc.AddHours(4), "PCS");
            var transformation = WorkOrderTransformation.CreateSplit(
                "org-001",
                "env-dev",
                Snapshot(parent),
                [Snapshot(child1), Snapshot(child2)],
                "split-postgres-001",
                "fingerprint-postgres-001",
                "user:planner-001",
                "按生产批次拆分",
                occurredAtUtc);

            parent.MarkSplit();
            setup.WorkOrders.AddRange(parent, child1, child2);
            setup.WorkOrderTransformations.Add(transformation);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        await using (var assertion = CreateContext(options))
        {
            var parent = await assertion.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-PARENT-001");
            var transformation = await assertion.WorkOrderTransformations
                .Include(x => x.Lines)
                .SingleAsync(x => x.IdempotencyKey == "split-postgres-001");

            Assert.Equal(WorkOrder.SplitStatus, parent.Status);
            Assert.Equal(2, parent.Version);
            Assert.Equal(WorkOrderTransformationStatus.Applied, transformation.Status);
            Assert.Equal("fingerprint-postgres-001", transformation.RequestFingerprint);
            Assert.Equal("user:planner-001", transformation.ActorId);
            Assert.Equal("按生产批次拆分", transformation.Reason);
            Assert.Equal(occurredAtUtc, transformation.OccurredAtUtc);
            Assert.Equal(10m, transformation.Lines.Sum(x => x.Quantity));
            Assert.Equal(2, transformation.Lines.Count);
            Assert.All(transformation.Lines, line =>
            {
                Assert.Equal(WorkOrderLineageType.Split, line.LineageType);
                Assert.Equal("WO-PARENT-001", line.SourceWorkOrderId);
                Assert.Equal("PCS", line.UomCode);
                Assert.Equal(1, line.SourceVersion);
                Assert.Equal(1, line.TargetVersion);
            });
        }
    }

    [MesRealPostgresFact]
    public async Task Merge_same_sku_small_orders_persists_source_to_target_lineage_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-25T11:00:00Z");

        await using (var setup = CreateContext(options))
        {
            await setup.Database.MigrateAsync(CancellationToken.None);

            var source1 = WorkOrder.Create(
                "org-001", "env-dev", "WO-MERGE-SOURCE-001", "SKU-001", "PV-001", 3m, 10,
                occurredAtUtc.AddHours(4), "PCS");
            var source2 = WorkOrder.Create(
                "org-001", "env-dev", "WO-MERGE-SOURCE-002", "SKU-001", "PV-001", 7m, 10,
                occurredAtUtc.AddHours(4), "PCS");
            var target = WorkOrder.Create(
                "org-001", "env-dev", "WO-MERGE-TARGET-001", "SKU-001", "PV-001", 10m, 10,
                occurredAtUtc.AddHours(4), "PCS");
            var transformation = WorkOrderTransformation.CreateMerge(
                "org-001",
                "env-dev",
                [Snapshot(source1), Snapshot(source2)],
                Snapshot(target),
                "merge-postgres-001",
                "fingerprint-merge-postgres-001",
                "user:planner-001",
                "合并同 SKU 小单",
                occurredAtUtc);

            source1.MarkMerged();
            source2.MarkMerged();
            setup.WorkOrders.AddRange(source1, source2, target);
            setup.WorkOrderTransformations.Add(transformation);
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertion = CreateContext(options);
        var sources = await assertion.WorkOrders
            .Where(x => x.WorkOrderIdValue.StartsWith("WO-MERGE-SOURCE-"))
            .ToListAsync();
        var transformationAfterReload = await assertion.WorkOrderTransformations
            .Include(x => x.Lines)
            .SingleAsync(x => x.IdempotencyKey == "merge-postgres-001");

        Assert.Equal(2, sources.Count);
        Assert.All(sources, source =>
        {
            Assert.Equal(WorkOrder.MergedStatus, source.Status);
            Assert.Equal(2, source.Version);
        });
        Assert.Equal(10m, transformationAfterReload.Lines.Sum(x => x.Quantity));
        Assert.Equal(2, transformationAfterReload.Lines.Count);
        Assert.All(transformationAfterReload.Lines, line =>
        {
            Assert.Equal(WorkOrderLineageType.Merge, line.LineageType);
            Assert.Equal("WO-MERGE-TARGET-001", line.TargetWorkOrderId);
            Assert.Equal("PCS", line.UomCode);
            Assert.Equal(1, line.SourceVersion);
            Assert.Equal(1, line.TargetVersion);
        });
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_uom_check_constraint_rejects_whitespace_lineage()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var setup = CreateContext(options);
        await setup.Database.MigrateAsync(CancellationToken.None);

        var parent = WorkOrder.Create(
            "org-001", "env-dev", "WO-UOM-PARENT-001", "SKU-001", "PV-001", 10m, 10,
            DateTimeOffset.Parse("2026-08-25T12:00:00Z"), "PCS");
        var child1 = WorkOrder.Create(
            "org-001", "env-dev", "WO-UOM-CHILD-001", "SKU-001", "PV-001", 5m, 10,
            DateTimeOffset.Parse("2026-08-25T12:00:00Z"), "PCS");
        var child2 = WorkOrder.Create(
            "org-001", "env-dev", "WO-UOM-CHILD-002", "SKU-001", "PV-001", 5m, 10,
            DateTimeOffset.Parse("2026-08-25T12:00:00Z"), "PCS");
        var transformation = WorkOrderTransformation.CreateSplit(
            "org-001",
            "env-dev",
            Snapshot(parent),
            [Snapshot(child1), Snapshot(child2)],
            "split-postgres-uom-001",
            "fingerprint-postgres-uom-001",
            "user:planner-001",
            "UOM 约束反例",
            DateTimeOffset.Parse("2026-08-25T12:00:00Z"));

        setup.WorkOrders.AddRange(parent, child1, child2);
        setup.WorkOrderTransformations.Add(transformation);
        await setup.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAnyAsync<DbException>(() => setup.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO mes.work_order_transformation_lines
                (id, organization_id, environment_id, lineage_type, source_work_order_id, target_work_order_id,
                 quantity, source_quantity, target_quantity, uom_code, source_status, target_status,
                 source_version, target_version, work_order_transformation_id)
            VALUES
                ({Guid.CreateVersion7()}, {"org-001"}, {"env-dev"}, {"Split"}, {parent.WorkOrderIdValue}, {child1.WorkOrderIdValue},
                 {5m}, {parent.Quantity}, {child1.Quantity}, {"   "}, {parent.Status}, {child1.Status},
                 {parent.Version}, {child1.Version}, {transformation.Id.Id});
            """, CancellationToken.None));

        Assert.Contains("ck_work_order_transformation_lines_uom_present", exception.Message, StringComparison.Ordinal);
    }

    [MesRealPostgresFact]
    public async Task Scoped_idempotency_key_is_enforced_by_postgresql()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        const string idempotencyKey = "split-postgres-scoped";
        await using (var setup = CreateContext(options))
        {
            await setup.Database.MigrateAsync(CancellationToken.None);
            setup.WorkOrders.AddRange(CreateAuditWorkOrders("org-001", "env-dev", "WO-AUDIT"));
            setup.WorkOrders.AddRange(CreateAuditWorkOrders("org-002", "env-dev", "WO-AUDIT-ORG-2"));
            setup.WorkOrders.AddRange(CreateAuditWorkOrders("org-001", "env-test", "WO-AUDIT-ENV-TEST"));
            await setup.SaveChangesAsync(CancellationToken.None);
            setup.WorkOrderTransformations.AddRange(
                CreateSplitAudit(idempotencyKey, "fingerprint-a"),
                CreateSplitAudit(idempotencyKey, "fingerprint-b", "org-002", "env-dev", "WO-AUDIT-ORG-2"),
                CreateSplitAudit(idempotencyKey, "fingerprint-c", "org-001", "env-test", "WO-AUDIT-ENV-TEST"));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        await using (var assertion = CreateContext(options))
        {
            var scopedTransformations = await assertion.WorkOrderTransformations
                .Where(x => x.IdempotencyKey == idempotencyKey)
                .ToListAsync();

            Assert.Equal(3, scopedTransformations.Count);
            Assert.Contains(scopedTransformations, x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev");
            Assert.Contains(scopedTransformations, x => x.OrganizationId == "org-002" && x.EnvironmentId == "env-dev");
            Assert.Contains(scopedTransformations, x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-test");
        }

        await using var duplicate = CreateContext(options);
        duplicate.WorkOrderTransformations.Add(CreateSplitAudit(idempotencyKey, "fingerprint-conflict"));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(CancellationToken.None));
    }

    [MesRealPostgresFact]
    public async Task Stale_work_order_version_is_rejected_by_postgresql_concurrency_token()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var setup = CreateContext(options))
        {
            await setup.Database.MigrateAsync(CancellationToken.None);
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-CONCURRENCY-001", "SKU-001", "PV-001", 10m, 10,
                DateTimeOffset.Parse("2026-08-25T10:00:00Z"), "PCS"));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        await using var winner = CreateContext(options);
        await using var stale = CreateContext(options);
        var winnerWorkOrder = await winner.WorkOrders.SingleAsync();
        var staleWorkOrder = await stale.WorkOrders.SingleAsync();

        winnerWorkOrder.MarkSplit();
        await winner.SaveChangesAsync(CancellationToken.None);

        staleWorkOrder.MarkMerged();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync(CancellationToken.None));
    }

    private static WorkOrderTransformation CreateSplitAudit(
        string idempotencyKey,
        string requestFingerprint,
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string workOrderPrefix = "WO-AUDIT")
    {
        var parent = Snapshot($"{workOrderPrefix}-PARENT", 10m);
        return WorkOrderTransformation.CreateSplit(
            organizationId,
            environmentId,
            parent,
            [Snapshot($"{workOrderPrefix}-CHILD-1", 5m), Snapshot($"{workOrderPrefix}-CHILD-2", 5m)],
            idempotencyKey,
            requestFingerprint,
            "user:planner-001",
            "持久化审计",
            DateTimeOffset.Parse("2026-08-25T10:00:00Z"));
    }

    private static WorkOrder[] CreateAuditWorkOrders(
        string organizationId,
        string environmentId,
        string workOrderPrefix)
    {
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        return
        [
            WorkOrder.Create(
                organizationId, environmentId, $"{workOrderPrefix}-PARENT", "SKU-001", "PV-001", 10m, 10,
                occurredAtUtc, "PCS"),
            WorkOrder.Create(
                organizationId, environmentId, $"{workOrderPrefix}-CHILD-1", "SKU-001", "PV-001", 5m, 10,
                occurredAtUtc, "PCS"),
            WorkOrder.Create(
                organizationId, environmentId, $"{workOrderPrefix}-CHILD-2", "SKU-001", "PV-001", 5m, 10,
                occurredAtUtc, "PCS"),
        ];
    }

    private static WorkOrderTransformationWorkOrderSnapshot Snapshot(WorkOrder workOrder) =>
        Snapshot(
            workOrder.WorkOrderIdValue,
            workOrder.SkuId,
            workOrder.ProductionVersionId,
            workOrder.UomCode,
            workOrder.Quantity,
            workOrder.Status,
            workOrder.Version);

    private static WorkOrderTransformationWorkOrderSnapshot Snapshot(
        string workOrderId,
        decimal quantity,
        string status = WorkOrder.CreatedStatus) =>
        Snapshot(workOrderId, "SKU-001", "PV-001", "PCS", quantity, status, 1);

    private static WorkOrderTransformationWorkOrderSnapshot Snapshot(
        string workOrderId,
        string skuId,
        string? productionVersionId,
        string? uomCode,
        decimal quantity,
        string status,
        long version) =>
        new(workOrderId, skuId, productionVersionId, uomCode, quantity, status, version);

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
