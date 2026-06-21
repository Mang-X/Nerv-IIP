using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using DomainScheduleResult = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduleResult;
using DomainScheduleTrigger = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduleTrigger;
using DomainScheduledOperationSnapshot = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduledOperationSnapshot;
using DomainWorkCenterUnavailability = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability;
using DomainDefectRecord = Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate.DefectRecord;
using DomainShiftHandover = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate.ShiftHandover;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

public sealed record MesAcceptedResponse(string Status, string ReferenceId, DateTimeOffset AcceptedAtUtc);

public sealed record MesOperationActionResponse(
    string OperationTaskId,
    string Status,
    DateTimeOffset ChangedAtUtc);

public sealed record ReleaseWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    DateTimeOffset ReleasedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class ReleaseWorkOrderCommandHandler(
    ApplicationDbContext dbContext,
    IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider = null)
    : ICommandHandler<ReleaseWorkOrderCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ReleaseWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken);

        if (workOrder is null)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        if (string.IsNullOrWhiteSpace(workOrder.ProductionVersionId))
        {
            throw new KnownException("QUALITY_PLAN_MISSING: 工单缺少已发布生产版本，无法放行。");
        }

        var hasOperationSnapshot = await dbContext.OperationTasks.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId,
            cancellationToken);
        if (!hasOperationSnapshot)
        {
            throw new KnownException($"工单缺少工艺路线快照，WorkOrderId = {request.WorkOrderId}");
        }

        var equipmentIssues = await ReadinessReasonCodes.GetEquipmentBlockingIssuesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            null,
            request.WorkOrderId,
            request.ReleasedAtUtc,
            cancellationToken);
        if (equipmentIssues.Count > 0)
        {
            throw new KnownException(string.Join("; ", equipmentIssues.Select(x => x.Code)));
        }

        var materialCapture = await MaterialReadinessGuards.EnsureRequirementSnapshotsAsync(
            dbContext,
            materialSnapshotProvider,
            workOrder,
            request.ReleasedAtUtc,
            cancellationToken);
        if (!materialCapture.NoRequirements)
        {
            var shortages = await MaterialReadinessGuards.GetShortageReasonsAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                null,
                cancellationToken);
            if (shortages.Count > 0)
            {
                throw new KnownException($"物料齐套未满足：{string.Join("; ", shortages)}");
            }
        }

        workOrder.MarkReleased();
        return new MesAcceptedResponse("Accepted", request.WorkOrderId, request.ReleasedAtUtc);
    }
}

public sealed record ConvertPlanToWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string ProductionPlanId,
    string? WorkOrderId,
    DateTimeOffset RequestedAtUtc,
    string SkuId,
    string? ProductionVersionId,
    decimal PlannedQuantity,
    string UomCode,
    DateTimeOffset DueUtc,
    string? WorkCenterId,
    string? SourceSystem = null,
    string? SourceDocumentType = null,
    string? SourceDocumentId = null,
    string? SourceDemandReference = null,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class ConvertPlanToWorkOrderCommandValidator : AbstractValidator<ConvertPlanToWorkOrderCommand>
{
    public ConvertPlanToWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductionPlanId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductionVersionId).MaximumLength(100);
        RuleFor(x => x.PlannedQuantity).GreaterThan(0);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        RuleFor(x => x.SourceSystem).MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(100);
        RuleFor(x => x.SourceDemandReference).MaximumLength(100);
    }
}

public sealed class ConvertPlanToWorkOrderCommandHandler : ICommandHandler<ConvertPlanToWorkOrderCommand, MesAcceptedResponse>
{
    private const int ConvertedPlanPriority = 100;
    private readonly ApplicationDbContext dbContext;
    private readonly RuleScheduler scheduler;
    private readonly MesCodingService _codingService;

    public ConvertPlanToWorkOrderCommandHandler(ApplicationDbContext dbContext, RuleScheduler scheduler, MesCodingService? codingService = null)
    {
        this.dbContext = dbContext;
        this.scheduler = scheduler;
        _codingService = codingService ?? new MesCodingService();
    }

    public ConvertPlanToWorkOrderCommandHandler(ApplicationDbContext dbContext)
        : this(dbContext, new RuleScheduler())
    {
    }

    public ConvertPlanToWorkOrderCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService)
        : this(dbContext, new RuleScheduler(), codingService)
    {
    }

    public async Task<MesAcceptedResponse> Handle(ConvertPlanToWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var sourceSystem = string.IsNullOrWhiteSpace(request.SourceSystem) ? "DemandPlanning" : request.SourceSystem.Trim();
        var sourceDocumentType = string.IsNullOrWhiteSpace(request.SourceDocumentType) ? "PlanningSuggestion" : request.SourceDocumentType.Trim();
        var sourceDocumentId = string.IsNullOrWhiteSpace(request.SourceDocumentId) ? request.ProductionPlanId.Trim() : request.SourceDocumentId.Trim();
        var allocation = await _codingService.AllocateWorkOrderIdAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(
                request.ProductionPlanId,
                request.WorkOrderId,
                request.SkuId,
                request.ProductionVersionId,
                request.PlannedQuantity,
                request.UomCode,
                request.DueUtc,
                request.WorkCenterId,
                sourceSystem,
                sourceDocumentType,
                sourceDocumentId,
                request.SourceDemandReference),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MesAcceptedResponse("Accepted", allocation.Code, request.RequestedAtUtc);
        }

        var alreadyExists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == allocation.Code,
            cancellationToken);
        if (alreadyExists)
        {
            throw new KnownException($"生产工单已存在，WorkOrderId = {allocation.Code}");
        }

        var sourceReference = new SourcePlanReference(
            sourceSystem,
            sourceDocumentType,
            sourceDocumentId,
            request.SourceDemandReference);
        var workOrder = WorkOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SkuId,
            request.ProductionVersionId,
            request.PlannedQuantity,
            ConvertedPlanPriority,
            request.DueUtc,
            request.UomCode,
            sourceReference);
        dbContext.WorkOrders.Add(workOrder);

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId))
        {
            var baselinePlan = scheduler.Schedule(
                await GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
                await GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));
            dbContext.OperationTasks.Add(OperationTask.Create(
                request.OrganizationId,
                request.EnvironmentId,
                allocation.Code,
                $"{allocation.Code}-OP-10",
                OperationTaskLifecycleStatus.Queued,
                10,
                request.WorkCenterId.Trim(),
                [],
                request.RequestedAtUtc,
                TimeSpan.FromMinutes(30),
                null,
                null));
            var plan = scheduler.Schedule(
                await GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
                await GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));
            await AddScheduleResultAsync(RescheduleTrigger.Manual, request.RequestedAtUtc, plan, baselinePlan.Assignments, cancellationToken);
        }

        return new MesAcceptedResponse("Accepted", allocation.Code, request.RequestedAtUtc);
    }

    private async Task<IReadOnlyCollection<ScheduleOperation>> GetScheduleOperationsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var persistedWorkOrders = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId)
            .ToListAsync(cancellationToken);
        var persistedWorkOrderIds = persistedWorkOrders.Select(x => x.Id).ToHashSet();
        var workOrders = persistedWorkOrders
            .Concat(dbContext.WorkOrders.Local.Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                !persistedWorkOrderIds.Contains(x.Id)))
            .GroupBy(x => x.WorkOrderIdValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        var workOrderIds = workOrders.Keys.ToArray();
        var persistedOperationTasks = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .ToListAsync(cancellationToken);
        var persistedOperationTaskIds = persistedOperationTasks.Select(x => x.Id).ToHashSet();
        var operationTasks = persistedOperationTasks
            .Concat(dbContext.OperationTasks.Local.Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                workOrders.ContainsKey(x.WorkOrderId) &&
                !persistedOperationTaskIds.Contains(x.Id)))
            .ToList();

        return operationTasks.Select(x =>
        {
            var workOrder = workOrders[x.WorkOrderId];
            return new ScheduleOperation(
                x.WorkOrderId,
                x.OperationTaskIdValue,
                ToWebStatus(x.Status),
                x.OperationSequence,
                workOrder.Priority,
                workOrder.DueUtc,
                x.EarliestStartUtc,
                x.Duration,
                x.WorkCenterId,
                x.AlternativeWorkCenterIdList,
                x.ExistingStartUtc,
                x.ExistingEndUtc);
        }).ToArray();
    }

    private async Task<IReadOnlyCollection<WorkCenterUnavailability>> GetUnavailabilitiesAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var persisted = await dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x =>
                (x.OrganizationId == null || x.OrganizationId == organizationId) &&
                (x.EnvironmentId == null || x.EnvironmentId == environmentId))
            .OrderBy(x => x.FromUtc)
            .ToListAsync(cancellationToken);
        var persistedIds = persisted.Select(x => x.Id).ToHashSet();
        return persisted
            .Concat(dbContext.WorkCenterUnavailabilities.Local.Where(x =>
                IsInScope(x, organizationId, environmentId) &&
                !persistedIds.Contains(x.Id)))
            .Select(x => new WorkCenterUnavailability(
                x.WorkCenterId,
                x.FromUtc,
                x.ToUtc,
                x.Reason,
                x.DeviceAssetId,
                x.OrganizationId,
                x.EnvironmentId))
            .ToArray();
    }

    private async Task AddScheduleResultAsync(
        RescheduleTrigger trigger,
        DateTimeOffset scheduledAtUtc,
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation> compareAssignments,
        CancellationToken cancellationToken)
    {
        var version = await dbContext.ScheduleResults.CountAsync(cancellationToken) + 1;
        var affectedWorkOrderIds = FindAffectedWorkOrders(plan, compareAssignments);
        dbContext.ScheduleResults.Add(DomainScheduleResult.Create(
            version,
            Enum.Parse<DomainScheduleTrigger>(trigger.ToString()),
            scheduledAtUtc,
            plan.Assignments.Select(x => new DomainScheduledOperationSnapshot(
                x.WorkOrderId,
                x.OperationTaskId,
                x.WorkCenterId,
                x.StartUtc,
                x.EndUtc,
                x.Reason)).ToArray(),
            affectedWorkOrderIds));
    }

    private static IReadOnlyCollection<string> FindAffectedWorkOrders(
        RuleSchedulePlan plan,
        IReadOnlyCollection<ScheduledOperation> compareAssignments)
    {
        var previousByTask = compareAssignments.ToDictionary(x => x.OperationTaskId, StringComparer.OrdinalIgnoreCase);
        return plan.Assignments
            .Where(x => previousByTask.TryGetValue(x.OperationTaskId, out var prior) && x.StartUtc > prior.StartUtc)
            .Select(x => x.WorkOrderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static OperationTaskStatus ToWebStatus(OperationTaskLifecycleStatus status) =>
        Enum.Parse<OperationTaskStatus>(status.ToString());

    private static bool IsInScope(DomainWorkCenterUnavailability unavailability, string organizationId, string environmentId)
    {
        var organizationMatches = unavailability.OrganizationId is null
            || string.Equals(unavailability.OrganizationId, organizationId, StringComparison.Ordinal);
        var environmentMatches = unavailability.EnvironmentId is null
            || string.Equals(unavailability.EnvironmentId, environmentId, StringComparison.Ordinal);
        return organizationMatches && environmentMatches;
    }
}

public sealed record CreateMaterialIssueRequestCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string? MaterialId,
    string? UomCode,
    decimal? Quantity,
    DateTimeOffset RequestedAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class CreateMaterialIssueRequestCommandValidator : AbstractValidator<CreateMaterialIssueRequestCommand>
{
    public CreateMaterialIssueRequestCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaterialId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Quantity).GreaterThan(0).When(x => x.Quantity.HasValue);
    }
}

public sealed class CreateMaterialIssueRequestCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<CreateMaterialIssueRequestCommand, MesAcceptedResponse>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<MesAcceptedResponse> Handle(CreateMaterialIssueRequestCommand request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken);

        if (!exists)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "material-issue-request",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.MaterialId, request.UomCode, request.Quantity, request.RequestedAtUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MesAcceptedResponse("Accepted", allocation.Code, request.RequestedAtUtc);
        }

        if (string.IsNullOrWhiteSpace(request.MaterialId))
        {
            throw new KnownException("领料申请必须指定物料，MaterialId 不能为空。");
        }

        var materialId = request.MaterialId.Trim();
        var uomCode = string.IsNullOrWhiteSpace(request.UomCode)
            ? throw new KnownException("领料申请必须指定单位，UomCode 不能为空。")
            : request.UomCode.Trim();

        var requestedQuantity = request.Quantity ?? await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                x.MaterialId == materialId)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Select(x => x.RequiredQuantity)
            .FirstOrDefaultAsync(cancellationToken);
        if (requestedQuantity <= 0)
        {
            throw new KnownException($"领料申请数量必须大于 0，WorkOrderId = {request.WorkOrderId}");
        }

        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.WorkOrderId,
            request.OperationTaskId,
            materialId,
            uomCode,
            requestedQuantity,
            request.RequestedAtUtc));
        return new MesAcceptedResponse("Accepted", allocation.Code, request.RequestedAtUtc);
    }
}

public sealed record ConfirmLineSideMaterialReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string RequestId,
    DateTimeOffset ReceivedAtUtc,
    decimal? ReceivedQuantity = null,
    string? MaterialLotId = null) : ICommand<MesAcceptedResponse>;

public sealed class ConfirmLineSideMaterialReceiptCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ConfirmLineSideMaterialReceiptCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ConfirmLineSideMaterialReceiptCommand request, CancellationToken cancellationToken)
    {
        var scopedQuery = dbContext.MaterialIssueRequests.Where(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId);
        var materialRequest = Guid.TryParse(request.RequestId, out var requestGuid)
            ? await scopedQuery.SingleOrDefaultAsync(x => x.Id.Id == requestGuid, cancellationToken)
            : await scopedQuery.SingleOrDefaultAsync(x => x.RequestNo == request.RequestId, cancellationToken);
        if (materialRequest is null)
        {
            throw new KnownException($"未找到领料申请，RequestId = {request.RequestId}");
        }

        materialRequest.ConfirmLineSideReceipt(request.ReceivedAtUtc, request.ReceivedQuantity, request.MaterialLotId);
        return new MesAcceptedResponse("Accepted", materialRequest.RequestNo, request.ReceivedAtUtc);
    }
}

public sealed record AssignDispatchTaskCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    DateTimeOffset AssignedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class AssignDispatchTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AssignDispatchTaskCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(AssignDispatchTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken);

        if (task is null)
        {
            throw new KnownException($"未找到工序任务，OperationTaskId = {request.OperationTaskId}");
        }

        var equipmentIssues = await ReadinessReasonCodes.GetEquipmentBlockingIssuesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            task.WorkCenterId,
            task.WorkOrderId,
            request.AssignedAtUtc,
            cancellationToken);
        if (equipmentIssues.Count > 0)
        {
            throw new KnownException(string.Join("; ", equipmentIssues.Select(x => x.Code)));
        }

        task.Assign(request.AssignedUserId, request.DeviceAssetId, request.ShiftId, request.AssignedAtUtc);
        dbContext.Entry(task).Property(x => x.AssignedUserId).IsModified = true;
        dbContext.Entry(task).Property(x => x.DeviceAssetId).IsModified = true;
        dbContext.Entry(task).Property(x => x.ShiftId).IsModified = true;
        dbContext.Entry(task).Property(x => x.AssignedAtUtc).IsModified = true;
        return new MesAcceptedResponse("Accepted", request.OperationTaskId, request.AssignedAtUtc);
    }
}

public sealed record ChangeOperationTaskStateCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string Action,
    DateTimeOffset ChangedAtUtc) : ICommand<MesOperationActionResponse>;

public sealed class ChangeOperationTaskStateCommandHandler(
    ApplicationDbContext dbContext,
    IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider = null)
    : ICommandHandler<ChangeOperationTaskStateCommand, MesOperationActionResponse>
{
    public async Task<MesOperationActionResponse> Handle(ChangeOperationTaskStateCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken)
            ?? throw new KnownException($"未找到工序任务，OperationTaskId = {request.OperationTaskId}");

        if (request.Action == "start")
        {
            var qualityIssues = await ReadinessReasonCodes.GetQualityBlockingIssuesAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                task.WorkOrderId,
                cancellationToken);
            if (qualityIssues.Count > 0)
            {
                throw new KnownException(string.Join("; ", qualityIssues.Select(x => x.Code)));
            }

            var equipmentIssues = await ReadinessReasonCodes.GetEquipmentBlockingIssuesAsync(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                task.WorkCenterId,
                task.WorkOrderId,
                request.ChangedAtUtc,
                cancellationToken);
            if (equipmentIssues.Count > 0)
            {
                throw new KnownException(string.Join("; ", equipmentIssues.Select(x => x.Code)));
            }

            var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderIdValue == task.WorkOrderId,
                cancellationToken)
                ?? throw new KnownException($"未找到生产工单，WorkOrderId = {task.WorkOrderId}");
            var materialCapture = await MaterialReadinessGuards.EnsureRequirementSnapshotsAsync(
                dbContext,
                materialSnapshotProvider,
                workOrder,
                request.ChangedAtUtc,
                cancellationToken);
            if (!materialCapture.NoRequirements)
            {
                var shortages = await MaterialReadinessGuards.GetShortageReasonsAsync(
                    dbContext,
                    request.OrganizationId,
                    request.EnvironmentId,
                    task.WorkOrderId,
                    task.OperationTaskIdValue,
                    cancellationToken);
                if (shortages.Count > 0)
                {
                    throw new KnownException($"物料齐套未满足：{string.Join("; ", shortages)}");
                }
            }
        }

        switch (request.Action)
        {
            case "start":
                task.Start(request.ChangedAtUtc);
                break;
            case "pause":
                task.Pause();
                break;
            case "resume":
                task.Resume(request.ChangedAtUtc);
                break;
            case "complete":
                task.Complete(request.ChangedAtUtc);
                break;
            default:
                throw new KnownException($"不支持的工序动作：{request.Action}");
        }

        return new MesOperationActionResponse(task.OperationTaskIdValue, task.Status.ToString(), request.ChangedAtUtc);
    }
}

internal static class MaterialReadinessGuards
{
    private const string MissingRequirementSnapshotReason =
        "MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照，无法确认物料齐套。";

    public static async Task<MaterialRequirementCaptureOutcome> EnsureRequirementSnapshotsAsync(
        ApplicationDbContext dbContext,
        IMesMaterialRequirementSnapshotProvider? snapshotProvider,
        WorkOrder workOrder,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        var hasRequirements = await HasRequirementSnapshotsAsync(
            dbContext,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.WorkOrderId,
            cancellationToken);
        if (hasRequirements)
        {
            return MaterialRequirementCaptureOutcome.Existing;
        }

        if (snapshotProvider is null)
        {
            return MaterialRequirementCaptureOutcome.Missing;
        }

        var result = await snapshotProvider.GetSnapshotAsync(
            new MesMaterialRequirementSnapshotRequest(
                workOrder.OrganizationId,
                workOrder.EnvironmentId,
                workOrder.WorkOrderId,
                workOrder.SkuId,
                workOrder.ProductionVersionId,
                workOrder.Quantity,
                capturedAtUtc),
            cancellationToken);
        if (result.Status == MesMaterialRequirementSnapshotStatus.Missing)
        {
            return MaterialRequirementCaptureOutcome.Missing;
        }

        if (result.Lines.Count == 0)
        {
            return MaterialRequirementCaptureOutcome.NoRequirementsFound;
        }

        foreach (var line in result.Lines)
        {
            dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
                workOrder.OrganizationId,
                workOrder.EnvironmentId,
                workOrder.WorkOrderId,
                line.OperationTaskId,
                line.MaterialId,
                line.MaterialLotId,
                line.RequiredQuantity,
                line.AvailableQuantity,
                line.StagedQuantity,
                result.SourceSystem,
                line.SourceSnapshotId,
                capturedAtUtc));
        }

        return MaterialRequirementCaptureOutcome.Captured;
    }

    public static async Task<IReadOnlyCollection<string>> GetShortageReasonsAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        string? operationTaskId,
        CancellationToken cancellationToken)
    {
        var persistedRequirements = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderId == workOrderId &&
                (operationTaskId == null || x.OperationTaskId == null || x.OperationTaskId == operationTaskId))
            .Select(x => new MaterialRequirementSnapshot(
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.StagedQuantity,
                x.CapturedAtUtc))
            .ToArrayAsync(cancellationToken);
        var requirements = persistedRequirements
            .Concat(dbContext.MaterialRequirements.Local
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.WorkOrderId == workOrderId &&
                    (operationTaskId == null || x.OperationTaskId == null || x.OperationTaskId == operationTaskId))
                .Select(x => new MaterialRequirementSnapshot(
                    x.OperationTaskId,
                    x.MaterialId,
                    x.MaterialLotId,
                    x.RequiredQuantity,
                    x.AvailableQuantity,
                    x.StagedQuantity,
                    x.CapturedAtUtc)))
            .ToArray();
        requirements = SelectLatestRequirementSnapshots(requirements);

        if (requirements.Length == 0)
        {
            return [MissingRequirementSnapshotReason];
        }

        var received = await dbContext.MaterialIssueRequests
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderId == workOrderId &&
                (operationTaskId == null || x.OperationTaskId == null || x.OperationTaskId == operationTaskId))
            .Select(x => new { x.MaterialId, x.MaterialLotId, x.ReceivedQuantity })
            .ToArrayAsync(cancellationToken);

        return requirements
            .GroupBy(x => new { x.MaterialId, x.MaterialLotId })
            .Select(x =>
            {
                var required = x.Sum(y => y.RequiredQuantity);
                var available = x.Sum(y => y.AvailableQuantity);
                var staged = x.Sum(y => y.StagedQuantity);
                var receivedQuantity = received
                    .Where(y =>
                        string.Equals(y.MaterialId, x.Key.MaterialId, StringComparison.OrdinalIgnoreCase) &&
                        (x.Key.MaterialLotId is null ||
                            string.Equals(y.MaterialLotId, x.Key.MaterialLotId, StringComparison.OrdinalIgnoreCase)))
                    .Sum(y => y.ReceivedQuantity);
                var shortage = Math.Max(0m, required - available - staged - receivedQuantity);
                return (x.Key.MaterialId, MaterialLotId: (string?)x.Key.MaterialLotId, Shortage: shortage);
            })
            .Where(x => x.Shortage > 0)
            .Select(x => x.MaterialLotId is null
                ? $"{x.MaterialId} shortage {x.Shortage:0.######}"
                : $"{x.MaterialId} {x.MaterialLotId} shortage {x.Shortage:0.######}")
            .ToArray();
    }

    private static async Task<bool> HasRequirementSnapshotsAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        return dbContext.MaterialRequirements.Local.Any(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderId == workOrderId) ||
            await dbContext.MaterialRequirements
                .AsNoTracking()
                .AnyAsync(
                    x => x.OrganizationId == organizationId &&
                        x.EnvironmentId == environmentId &&
                        x.WorkOrderId == workOrderId,
                    cancellationToken);
    }

    public readonly record struct MaterialRequirementCaptureOutcome(bool NoRequirements)
    {
        public static MaterialRequirementCaptureOutcome Existing { get; } = new(false);

        public static MaterialRequirementCaptureOutcome Captured { get; } = new(false);

        public static MaterialRequirementCaptureOutcome Missing { get; } = new(false);

        public static MaterialRequirementCaptureOutcome NoRequirementsFound { get; } = new(true);
    }

    internal static T[] SelectLatestRequirementSnapshots<T>(IEnumerable<T> requirements)
        where T : IMaterialRequirementSnapshot
    {
        return requirements
            .GroupBy(
                x => $"{x.OperationTaskId?.ToUpperInvariant()}|{x.MaterialId.ToUpperInvariant()}|{x.MaterialLotId?.ToUpperInvariant()}",
                StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(y => y.CapturedAtUtc).First())
            .ToArray();
    }

    internal interface IMaterialRequirementSnapshot
    {
        string? OperationTaskId { get; }

        string MaterialId { get; }

        string? MaterialLotId { get; }

        DateTimeOffset CapturedAtUtc { get; }
    }

    internal sealed record MaterialRequirementSnapshot(
        string? OperationTaskId,
        string MaterialId,
        string? MaterialLotId,
        decimal RequiredQuantity,
        decimal AvailableQuantity,
        decimal StagedQuantity,
        DateTimeOffset CapturedAtUtc) : IMaterialRequirementSnapshot;
}

internal sealed record ReadinessBlockingIssue(
    string Code,
    string SourceSystem,
    string ReferenceType,
    string ReferenceId,
    string Message);

internal static class ReadinessReasonCodes
{
    public static async Task<IReadOnlyCollection<ReadinessBlockingIssue>> GetQualityBlockingIssuesAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        var productionVersionId = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderIdValue == workOrderId)
            .Select(x => x.ProductionVersionId)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(productionVersionId)
            ? [
                new ReadinessBlockingIssue(
                    MesReadinessReasonCodes.QualityPlanMissing,
                    "Quality",
                    "InspectionPlan",
                    workOrderId,
                    "工单缺少已发布生产版本或检验方案。"),
            ]
            : [];
    }

    public static async Task<IReadOnlyCollection<ReadinessBlockingIssue>> GetEquipmentBlockingIssuesAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workCenterId,
        string? workOrderId,
        DateTimeOffset effectiveAtUtc,
        CancellationToken cancellationToken)
    {
        var scopedQuery = dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.FromUtc <= effectiveAtUtc &&
                (x.ToUtc == null || x.ToUtc > effectiveAtUtc));

        if (!string.IsNullOrWhiteSpace(workCenterId))
        {
            scopedQuery = scopedQuery.Where(x => x.WorkCenterId == workCenterId);
        }
        else if (!string.IsNullOrWhiteSpace(workOrderId))
        {
            var taskWorkCenters = dbContext.OperationTasks
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.WorkOrderId == workOrderId)
                .Select(x => x.WorkCenterId);
            scopedQuery = scopedQuery.Where(x => taskWorkCenters.Contains(x.WorkCenterId));
        }

        var unavailabilities = await scopedQuery
            .OrderBy(x => x.FromUtc)
            .Select(x => new { x.DowntimeEventNo, x.WorkCenterId, x.Reason })
            .ToArrayAsync(cancellationToken);

        return unavailabilities
            .Select(x =>
            {
                var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(x.Reason);
                return new ReadinessBlockingIssue(
                    classification.Code,
                    classification.SourceSystem,
                    "DowntimeEvent",
                    x.DowntimeEventNo,
                    $"设备或工作中心存在维护/报警/停机冲突，WorkCenterId = {x.WorkCenterId}");
            })
            .ToArray();
    }
}

public sealed record RecordDefectCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? OperationTaskId,
    string DefectCode,
    decimal Quantity,
    DateTimeOffset RecordedAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class RecordDefectCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<RecordDefectCommand, MesAcceptedResponse>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<MesAcceptedResponse> Handle(RecordDefectCommand request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken);

        if (!exists)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "defect",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.DefectCode, request.Quantity, request.RecordedAtUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MesAcceptedResponse("Accepted", allocation.Code, request.RecordedAtUtc);
        }

        var defect = DomainDefectRecord.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.WorkOrderId,
            request.OperationTaskId,
            request.DefectCode,
            request.Quantity,
            request.RecordedAtUtc);
        dbContext.DefectRecords.Add(defect);
        return new MesAcceptedResponse("Accepted", defect.DefectNo, request.RecordedAtUtc);
    }
}

public sealed record RecordDowntimeEventCommand(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string? OperationTaskId,
    string WorkCenterId,
    string? DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class RecordDowntimeEventCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<RecordDowntimeEventCommand, MesAcceptedResponse>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<MesAcceptedResponse> Handle(RecordDowntimeEventCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "downtime-event",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.WorkCenterId, request.DeviceAssetId, request.Reason, request.FromUtc, request.ToUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MesAcceptedResponse("Accepted", allocation.Code, request.FromUtc);
        }

        var downtime = DomainWorkCenterUnavailability.Open(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.WorkCenterId,
            request.FromUtc,
            request.ToUtc,
            request.Reason,
            request.DeviceAssetId);
        dbContext.WorkCenterUnavailabilities.Add(downtime);
        await Task.CompletedTask;
        return new MesAcceptedResponse("Accepted", downtime.DowntimeEventNo, request.FromUtc);
    }
}

public sealed record ConfirmDowntimeRecoveryCommand(
    string OrganizationId,
    string EnvironmentId,
    string DowntimeEventId,
    DateTimeOffset RecoveredAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class ConfirmDowntimeRecoveryCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ConfirmDowntimeRecoveryCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ConfirmDowntimeRecoveryCommand request, CancellationToken cancellationToken)
    {
        var downtime = await dbContext.WorkCenterUnavailabilities.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                (x.Id.Id.ToString() == request.DowntimeEventId ||
                    x.DowntimeEventNo == request.DowntimeEventId),
            cancellationToken)
            ?? throw new KnownException($"未找到停机事件，DowntimeEventId = {request.DowntimeEventId}");

        downtime.Close(request.RecoveredAtUtc);
        return new MesAcceptedResponse("Accepted", request.DowntimeEventId, request.RecoveredAtUtc);
    }
}

public sealed record CreateShiftHandoverCommand(
    string OrganizationId,
    string EnvironmentId,
    string ShiftId,
    string TeamId,
    DateTimeOffset HandoverAtUtc,
    string? IdempotencyKey = null) : ICommand<MesAcceptedResponse>;

public sealed class CreateShiftHandoverCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<CreateShiftHandoverCommand, MesAcceptedResponse>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<MesAcceptedResponse> Handle(CreateShiftHandoverCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "shift-handover",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(request.ShiftId, request.TeamId, request.HandoverAtUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new MesAcceptedResponse("Accepted", allocation.Code, request.HandoverAtUtc);
        }

        var openIssueCount = await CountOpenHandoverIssuesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.HandoverAtUtc,
            cancellationToken);
        var handover = DomainShiftHandover.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.ShiftId,
            request.TeamId,
            openIssueCount,
            request.HandoverAtUtc);
        dbContext.ShiftHandovers.Add(handover);
        return new MesAcceptedResponse("Accepted", handover.HandoverNo, request.HandoverAtUtc);
    }

    private async Task<int> CountOpenHandoverIssuesAsync(
        string organizationId,
        string environmentId,
        DateTimeOffset effectiveAtUtc,
        CancellationToken cancellationToken)
    {
        // This is an environment-level handover snapshot. Shift/team scoped ownership is not available for every source fact yet.
        var openDefects = await dbContext.DefectRecords.CountAsync(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.ClosedAtUtc == null,
            cancellationToken);
        var openDowntimeEvents = await dbContext.WorkCenterUnavailabilities.CountAsync(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.FromUtc <= effectiveAtUtc &&
                (x.ToUtc == null || x.ToUtc > effectiveAtUtc),
            cancellationToken);
        var openMaterialIssues = await dbContext.MaterialIssueRequests.CountAsync(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.Status != MaterialIssueRequest.ReceivedStatus,
            cancellationToken);
        return openDefects + openDowntimeEvents + openMaterialIssues;
    }
}

public sealed record AcceptShiftHandoverCommand(
    string OrganizationId,
    string EnvironmentId,
    string HandoverId,
    DateTimeOffset AcceptedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class AcceptShiftHandoverCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AcceptShiftHandoverCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(AcceptShiftHandoverCommand request, CancellationToken cancellationToken)
    {
        var handover = await dbContext.ShiftHandovers.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                (x.HandoverNo == request.HandoverId || x.Id.Id.ToString() == request.HandoverId),
            cancellationToken)
            ?? throw new KnownException($"未找到班次交接，HandoverId = {request.HandoverId}");

        try
        {
            handover.Accept(request.AcceptedAtUtc);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message);
        }

        return new MesAcceptedResponse("Accepted", handover.HandoverNo, handover.AcceptedAtUtc ?? request.AcceptedAtUtc);
    }
}
