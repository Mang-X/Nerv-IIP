using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

public sealed record MesOperationTaskActionReadiness(
    IReadOnlyCollection<string> AllowedActions,
    IReadOnlyCollection<string> BlockReasons,
    DateTimeOffset EvaluatedAtUtc);

public sealed class MesOperationTaskActionReadinessEvaluator(
    ApplicationDbContext dbContext)
{
    public async Task<MesOperationTaskActionReadiness> EvaluateAsync(
        OperationTask task,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var evaluated = await EvaluateManyAsync(
            [task],
            evaluatedAtUtc,
            cancellationToken);
        return evaluated[task.OperationTaskIdValue];
    }

    public async Task<IReadOnlyDictionary<string, MesOperationTaskActionReadiness>> EvaluateManyAsync(
        IReadOnlyCollection<OperationTask> tasks,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return new Dictionary<string, MesOperationTaskActionReadiness>(StringComparer.Ordinal);
        }

        var organizationId = tasks.First().OrganizationId;
        var environmentId = tasks.First().EnvironmentId;
        var workOrderIds = tasks.Select(x => x.WorkOrderId).Distinct(StringComparer.Ordinal).ToArray();
        var workCenterIds = tasks.Select(x => x.WorkCenterId).Distinct(StringComparer.Ordinal).ToArray();

        var allOperations = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new OperationFact(
                x.WorkOrderId,
                x.OperationTaskIdValue,
                x.OperationSequence,
                x.Status))
            .ToArrayAsync(cancellationToken);
        var workOrders = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderIdValue))
            .Select(x => new WorkOrderFact(
                x.WorkOrderIdValue,
                x.ProductionVersionId,
                x.MaterialRequirementSnapshotStatus,
                x.MaterialRequirementSnapshotProductionVersionId))
            .ToArrayAsync(cancellationToken);
        var activeQualityHolds = await dbContext.QualityHoldContexts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId) &&
                x.Active)
            .Select(x => new QualityHoldFact(
                x.WorkOrderId,
                x.OperationTaskId,
                x.DispositionReason))
            .ToArrayAsync(cancellationToken);
        var activeUnavailabilities = await dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workCenterIds.Contains(x.WorkCenterId) &&
                x.FromUtc <= evaluatedAtUtc &&
                (x.ToUtc == null || x.ToUtc > evaluatedAtUtc))
            .Select(x => new UnavailabilityFact(x.WorkCenterId, x.Reason))
            .ToArrayAsync(cancellationToken);
        var persistedRequirements = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new MaterialRequirementFact(
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.StagedQuantity,
                x.CapturedAtUtc))
            .ToArrayAsync(cancellationToken);
        var localRequirements = dbContext.MaterialRequirements.Local
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new MaterialRequirementFact(
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.StagedQuantity,
                x.CapturedAtUtc));
        var requirements = persistedRequirements
            .Concat(localRequirements)
            .GroupBy(
                x => $"{x.WorkOrderId.ToUpperInvariant()}|{x.OperationTaskId?.ToUpperInvariant()}|{x.MaterialId.ToUpperInvariant()}|{x.MaterialLotId?.ToUpperInvariant()}",
                StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(y => y.CapturedAtUtc).First())
            .ToArray();
        var receipts = await dbContext.MaterialIssueRequests
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new ReceiptFact(
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.ReceivedQuantity))
            .ToArrayAsync(cancellationToken);

        var workOrderMap = workOrders.ToDictionary(x => x.WorkOrderIdValue, StringComparer.Ordinal);
        var result = new Dictionary<string, MesOperationTaskActionReadiness>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            result[task.OperationTaskIdValue] = Evaluate(
                task,
                evaluatedAtUtc,
                allOperations,
                workOrderMap,
                activeQualityHolds,
                activeUnavailabilities,
                requirements,
                receipts);
        }

        return result;
    }

    private static MesOperationTaskActionReadiness Evaluate(
        OperationTask task,
        DateTimeOffset evaluatedAtUtc,
        IReadOnlyCollection<OperationFact> allOperations,
        IReadOnlyDictionary<string, WorkOrderFact> workOrders,
        IReadOnlyCollection<QualityHoldFact> activeQualityHolds,
        IReadOnlyCollection<UnavailabilityFact> activeUnavailabilities,
        IReadOnlyCollection<MaterialRequirementFact> requirements,
        IReadOnlyCollection<ReceiptFact> receipts)
    {
        if (task.Status == OperationTaskLifecycleStatus.InProgress)
        {
            return new(["pause", "complete", "report"], [], evaluatedAtUtc);
        }

        if (task.Status == OperationTaskLifecycleStatus.Paused)
        {
            return new(["resume"], [], evaluatedAtUtc);
        }

        if (task.Status != OperationTaskLifecycleStatus.Queued)
        {
            return new([], [], evaluatedAtUtc);
        }

        var blockReasons = new List<string>();
        var previousOperations = allOperations
            .Where(x =>
                x.WorkOrderId == task.WorkOrderId &&
                x.OperationSequence < task.OperationSequence &&
                x.Status != OperationTaskLifecycleStatus.Completed)
            .OrderBy(x => x.OperationSequence)
            .Select(x => x.OperationTaskIdValue)
            .ToArray();
        if (previousOperations.Length > 0)
        {
            blockReasons.Add(
                $"PREVIOUS_OPERATION_INCOMPLETE: 前序工序尚未完成（{string.Join('、', previousOperations)}）");
        }

        if (!workOrders.TryGetValue(task.WorkOrderId, out var workOrder))
        {
            blockReasons.Add("WORK_ORDER_NOT_FOUND: 未找到所属生产工单");
        }
        else if (string.IsNullOrWhiteSpace(workOrder.ProductionVersionId))
        {
            blockReasons.Add($"{MesReadinessReasonCodes.QualityPlanMissing}: 工单缺少已发布生产版本或检验方案");
        }

        foreach (var hold in activeQualityHolds.Where(x =>
                     x.WorkOrderId == task.WorkOrderId &&
                     (x.OperationTaskId == null || x.OperationTaskId == task.OperationTaskIdValue)))
        {
            var detail = string.IsNullOrWhiteSpace(hold.DispositionReason)
                ? "工单存在有效质量保留，无法开工"
                : $"工单存在有效质量保留，无法开工：{hold.DispositionReason}";
            blockReasons.Add($"{MesReadinessReasonCodes.QualityHoldActive}: {detail}");
        }

        foreach (var unavailable in activeUnavailabilities.Where(x => x.WorkCenterId == task.WorkCenterId))
        {
            var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(unavailable.Reason);
            blockReasons.Add($"{classification.Code}: {classification.Message}");
        }

        var scopedRequirements = requirements
            .Where(x =>
                x.WorkOrderId == task.WorkOrderId &&
                (x.OperationTaskId == null || x.OperationTaskId == task.OperationTaskIdValue))
            .ToArray();
        var expectedSnapshotStatus = scopedRequirements.Length == 0
            ? WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus
            : WorkOrder.MaterialRequirementSnapshotCapturedStatus;
        var materialSnapshotProven =
            workOrders.TryGetValue(task.WorkOrderId, out var materialWorkOrder)
            && string.Equals(
                materialWorkOrder.MaterialRequirementSnapshotStatus,
                expectedSnapshotStatus,
                StringComparison.Ordinal)
            && string.Equals(
                materialWorkOrder.MaterialRequirementSnapshotProductionVersionId,
                materialWorkOrder.ProductionVersionId,
                StringComparison.Ordinal);
        if (!materialSnapshotProven)
        {
            blockReasons.Add(MaterialReadinessGuards.MissingRequirementSnapshotReason);
        }

        if (scopedRequirements.Length > 0)
        {
            foreach (var group in scopedRequirements.GroupBy(x => new { x.MaterialId, x.MaterialLotId }))
            {
                var receivedQuantity = receipts
                    .Where(x =>
                        x.WorkOrderId == task.WorkOrderId &&
                        (x.OperationTaskId == null || x.OperationTaskId == task.OperationTaskIdValue) &&
                        string.Equals(x.MaterialId, group.Key.MaterialId, StringComparison.OrdinalIgnoreCase) &&
                        (group.Key.MaterialLotId == null ||
                            string.Equals(x.MaterialLotId, group.Key.MaterialLotId, StringComparison.OrdinalIgnoreCase)))
                    .Sum(x => x.ReceivedQuantity);
                var shortage = Math.Max(
                    0m,
                    group.Sum(x => x.RequiredQuantity) -
                    group.Sum(x => x.AvailableQuantity) -
                    group.Sum(x => x.StagedQuantity) -
                    receivedQuantity);
                if (shortage > 0m)
                {
                    var lot = group.Key.MaterialLotId is null ? string.Empty : $"，批次 {group.Key.MaterialLotId}";
                    blockReasons.Add(
                        $"MATERIAL_SHORTAGE: 物料齐套未满足，物料 {group.Key.MaterialId}{lot} 缺口 {shortage:0.######}");
                }
            }
        }

        var canonicalReasons = blockReasons.Distinct(StringComparer.Ordinal).ToArray();
        return new(
            canonicalReasons.Length == 0 ? ["start"] : [],
            canonicalReasons,
            evaluatedAtUtc);
    }

    private sealed record MaterialRequirementFact(
        string WorkOrderId,
        string? OperationTaskId,
        string MaterialId,
        string? MaterialLotId,
        decimal RequiredQuantity,
        decimal AvailableQuantity,
        decimal StagedQuantity,
        DateTimeOffset CapturedAtUtc);

    private sealed record OperationFact(
        string WorkOrderId,
        string OperationTaskIdValue,
        int OperationSequence,
        OperationTaskLifecycleStatus Status);

    private sealed record WorkOrderFact(
        string WorkOrderIdValue,
        string? ProductionVersionId,
        string? MaterialRequirementSnapshotStatus,
        string? MaterialRequirementSnapshotProductionVersionId);

    private sealed record QualityHoldFact(
        string WorkOrderId,
        string? OperationTaskId,
        string? DispositionReason);

    private sealed record UnavailabilityFact(string WorkCenterId, string Reason);

    private sealed record ReceiptFact(
        string WorkOrderId,
        string? OperationTaskId,
        string MaterialId,
        string? MaterialLotId,
        decimal ReceivedQuantity);
}
