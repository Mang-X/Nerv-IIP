using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Mes.Web.Tests;

internal static class NcrReworkRequestedPostgresFixtures
{
    public static async Task SeedSourceAsync(
        IServiceProvider provider,
        string organizationId,
        string environmentId,
        string sourceWorkOrderId = "WO-SOURCE-001",
        string defectNo = "DEF-001",
        string operationTaskPrefix = "OP-SOURCE",
        bool workOrderLevelDefect = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sourceWorkOrder = WorkOrder.Create(
            organizationId,
            environmentId,
            sourceWorkOrderId,
            "SKU-001",
            "PV-001",
            10m,
            100,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            "PCS");
        var sourceOperations = new[]
        {
            OperationTask.Queue(
                organizationId,
                environmentId,
                sourceWorkOrderId,
                $"{operationTaskPrefix}-10",
                10,
                "WC-010",
                ["WC-010-B"],
                DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
                TimeSpan.FromMinutes(10),
                "SKU-001",
                "PCS",
                10m,
                false,
                "OP-CODE-010"),
            OperationTask.Create(
                organizationId,
                environmentId,
                sourceWorkOrderId,
                $"{operationTaskPrefix}-20",
                OperationTaskLifecycleStatus.Completed,
                20,
                "WC-020",
                ["WC-020-B", "WC-020-C"],
                DateTimeOffset.Parse("2026-08-29T08:10:00Z"),
                TimeSpan.FromMinutes(20),
                DateTimeOffset.Parse("2026-08-29T07:00:00Z"),
                DateTimeOffset.Parse("2026-08-29T07:20:00Z"),
                "SKU-001",
                "PCS",
                10m,
                true,
                "OP-CODE-020"),
            OperationTask.Create(
                organizationId,
                environmentId,
                sourceWorkOrderId,
                $"{operationTaskPrefix}-30",
                OperationTaskLifecycleStatus.InProgress,
                30,
                "WC-030",
                [],
                DateTimeOffset.Parse("2026-08-29T08:30:00Z"),
                TimeSpan.FromMinutes(30),
                DateTimeOffset.Parse("2026-08-29T07:20:00Z"),
                null,
                "SKU-001",
                "PCS",
                10m,
                false,
                "OP-CODE-030"),
        };
        sourceWorkOrder.MarkReleased(sourceOperations);
        db.WorkOrders.Add(sourceWorkOrder);
        db.OperationTasks.AddRange(sourceOperations);
        db.DefectRecords.Add(DefectRecord.Create(
            organizationId,
            environmentId,
            defectNo,
            sourceWorkOrderId,
            workOrderLevelDefect ? null : $"{operationTaskPrefix}-20",
            "surface-defect",
            3m,
            DateTimeOffset.Parse("2026-08-29T07:00:00Z")));
        await db.SaveChangesAsync();
    }

    public static NcrReworkRequestedIntegrationEvent CreateEvent(
        string eventId = "evt-rework-001",
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string ncrId = "ncr-001",
        string ncrCode = "NCR-2026-0001",
        string skuCode = "SKU-001",
        decimal quantity = 3m,
        string sourceDefectNo = "DEF-001",
        string idempotencyKey = "quality:rework:org-001:env-dev:ncr-001",
        DateTimeOffset? requestedAtUtc = null) => new(
            eventId,
            QualityIntegrationEventTypes.NcrReworkRequested,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            organizationId,
            environmentId,
            "user:quality-manager",
            idempotencyKey,
            new NcrReworkRequestedPayload(
                ncrId,
                ncrCode,
                sourceDefectNo,
                skuCode,
                quantity,
                "LOT-001",
                "SN-001",
                requestedAtUtc ?? DateTimeOffset.Parse("2026-08-29T08:00:00Z")));
}
