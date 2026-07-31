using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using WorkCenterUnavailabilityId = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailabilityId;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.MasterData;
using DomainScheduleResult = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduleResult;
using DomainScheduleTrigger = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduleTrigger;
using DomainScheduledOperationSnapshot = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.ScheduledOperationSnapshot;
using DomainWorkCenterUnavailability = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability;
using DomainDefectRecord = Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate.DefectRecord;
using DomainShiftHandover = Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate.ShiftHandover;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Coding;

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

        WorkOrderLifecycleCommandGuards.EnsureActionAllowed(
            workOrder,
            "release",
            WorkOrder.CreatedStatus,
            WorkOrder.StartedStatus,
            WorkOrder.HoldStatus);

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

        var qualityIssues = await ReadinessReasonCodes.GetQualityBlockingIssuesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            null,
            cancellationToken);
        if (qualityIssues.Count > 0)
        {
            throw new KnownException(string.Join("; ", qualityIssues.Select(x => x.Code)));
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
                throw new KnownException($"物料齐套未满足：{MaterialReadinessGuards.DescribeForUser(shortages)}");
            }
        }

        workOrder.MarkReleased();
        return new MesAcceptedResponse("Accepted", request.WorkOrderId, request.ReleasedAtUtc);
    }
}

public sealed record ForceReleaseQualityHoldCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceService,
    string SourceDocumentId,
    string Reason,
    string Actor,
    string CorrelationId,
    string IdempotencyKey,
    DateTimeOffset ReleasedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class ForceReleaseQualityHoldCommandValidator : AbstractValidator<ForceReleaseQualityHoldCommand>
{
    public ForceReleaseQualityHoldCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceService).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Actor).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CorrelationId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(512);
    }
}

public sealed class ForceReleaseQualityHoldCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ForceReleaseQualityHoldCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ForceReleaseQualityHoldCommand request, CancellationToken cancellationToken)
    {
        var hold = await dbContext.QualityHoldContexts.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceService == request.SourceService &&
                x.SourceDocumentId == request.SourceDocumentId,
            cancellationToken);

        if (hold is null)
        {
            throw new KnownException($"未找到质量保留上下文，SourceDocumentId = {request.SourceDocumentId}");
        }

        var replayed = await dbContext.QualityHoldTransitions.AsNoTracking().SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceService == request.SourceService &&
                x.SourceDocumentId == request.SourceDocumentId &&
                x.Origin == "manual" &&
                x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (replayed is not null)
        {
            if (replayed.CorrelationId != request.CorrelationId ||
                replayed.Actor != request.Actor ||
                replayed.OccurredAtUtc != request.ReleasedAtUtc ||
                replayed.Reason != request.Reason)
            {
                throw new KnownException("Quality hold transition idempotency key was reused with a different payload.");
            }
            return new MesAcceptedResponse("Accepted", request.SourceDocumentId, request.ReleasedAtUtc);
        }

        if (hold.ForceRelease(request.Reason, request.Actor, request.ReleasedAtUtc))
        {
            dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
                request.OrganizationId, request.EnvironmentId, request.SourceService, request.SourceDocumentId,
                hold.HeldInspectionRecordId!, request.CorrelationId,
                "manual-force-released", request.Actor, request.ReleasedAtUtc, request.Reason,
                hold.HeldInspectionRecordId, hold.HeldInspectionDocumentId, "manual", request.IdempotencyKey));
        }
        else
        {
            dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
                request.OrganizationId, request.EnvironmentId, request.SourceService, request.SourceDocumentId,
                hold.HeldInspectionRecordId ?? hold.InspectionRecordId, request.CorrelationId,
                "manual-force-release-noop", request.Actor, request.ReleasedAtUtc, request.Reason,
                hold.HeldInspectionRecordId ?? hold.InspectionRecordId,
                hold.HeldInspectionDocumentId ?? hold.InspectionPlanId,
                "manual", request.IdempotencyKey));
        }
        return new MesAcceptedResponse("Accepted", request.SourceDocumentId, request.ReleasedAtUtc);
    }
}

public sealed record CloseWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    DateTimeOffset ClosedAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class CloseWorkOrderCommandValidator : AbstractValidator<CloseWorkOrderCommand>
{
    public CloseWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
    }
}

public sealed class CloseWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CloseWorkOrderCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(CloseWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLifecycleCommandGuards.GetWorkOrderAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            cancellationToken);

        WorkOrderLifecycleCommandGuards.ApplyTransition(workOrder, x => x.Close(request.ClosedAtUtc));

        return new MesAcceptedResponse("Accepted", workOrder.WorkOrderId, request.ClosedAtUtc);
    }
}

public sealed record HoldWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string Reason,
    DateTimeOffset HeldAtUtc) : ICommand<MesAcceptedResponse>;

public sealed class HoldWorkOrderCommandValidator : AbstractValidator<HoldWorkOrderCommand>
{
    public HoldWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class HoldWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<HoldWorkOrderCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(HoldWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLifecycleCommandGuards.GetWorkOrderAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            cancellationToken);

        WorkOrderLifecycleCommandGuards.EnsureActionAllowed(
            workOrder,
            "hold",
            WorkOrder.CreatedStatus,
            WorkOrder.ReleasedStatus,
            WorkOrder.StartedStatus,
            WorkOrder.HoldStatus);
        WorkOrderLifecycleCommandGuards.ApplyTransition(workOrder, x => x.Hold(request.Reason));

        return new MesAcceptedResponse("Accepted", workOrder.WorkOrderId, request.HeldAtUtc);
    }
}

public sealed record CancelWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string Reason,
    DateTimeOffset CancelledAtUtc,
    string Actor = "system:mes") : ICommand<MesAcceptedResponse>, IOperationTaskConcurrencyRetryCommand;

public sealed class CancelWorkOrderCommandValidator : AbstractValidator<CancelWorkOrderCommand>
{
    public CancelWorkOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CancelWorkOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CancelWorkOrderCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(CancelWorkOrderCommand request, CancellationToken cancellationToken)
    {
        return await WorkOrderCancellationOrchestrator.CancelAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.Reason,
            request.CancelledAtUtc,
            cancellationToken,
            request.Actor);
    }
}

internal static class WorkOrderCancellationOrchestrator
{
    public static async Task<MesAcceptedResponse> CancelAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        string reason,
        DateTimeOffset cancelledAtUtc,
        CancellationToken cancellationToken,
        string actor = "system:mes")
    {
        var workOrder = await WorkOrderLifecycleCommandGuards.GetWorkOrderAsync(
            dbContext,
            organizationId,
            environmentId,
            workOrderId,
            cancellationToken);

        var workOrderAlreadyCancelled = workOrder.Status == WorkOrder.CancelledStatus;
        if (!workOrderAlreadyCancelled)
        {
            WorkOrderLifecycleCommandGuards.EnsureActionAllowed(
                workOrder,
                "cancel",
                WorkOrder.CreatedStatus,
                WorkOrder.ReleasedStatus,
                WorkOrder.StartedStatus,
                WorkOrder.HoldStatus);
        }

        var materialIssueRequests = await dbContext.MaterialIssueRequests
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);
        var finishedGoodsReceiptRequests = await dbContext.FinishedGoodsReceiptRequests
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);
        var operationTasks = await dbContext.OperationTasks
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);

        if (!workOrderAlreadyCancelled)
        {
            WorkOrderLifecycleCommandGuards.ApplyTransition(workOrder, x => x.Cancel(
                reason,
                cancelledAtUtc,
                materialIssueRequests.Select(materialIssueRequest => materialIssueRequest.RequestNo).ToArray()));
        }

        foreach (var materialIssueRequest in materialIssueRequests)
        {
            try
            {
                var consumedQuantity = await dbContext.ProductionReportMaterialConsumptions
                    .AsNoTracking()
                    .Where(x =>
                        x.OrganizationId == materialIssueRequest.OrganizationId &&
                        x.EnvironmentId == materialIssueRequest.EnvironmentId &&
                        x.MaterialIssueRequestNo == materialIssueRequest.RequestNo &&
                        x.MaterialId == materialIssueRequest.MaterialId &&
                        x.MaterialLotId == materialIssueRequest.MaterialLotId)
                    .SumAsync(x => x.ConsumedQuantity, cancellationToken);
                materialIssueRequest.CancelForWorkOrderCancellation(cancelledAtUtc, consumedQuantity);
            }
            catch (InvalidOperationException exception)
            {
                throw new KnownException(exception.Message, exception);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new KnownException(exception.Message, exception);
            }
        }

        foreach (var receiptRequest in finishedGoodsReceiptRequests)
        {
            receiptRequest.Cancel();
        }

        foreach (var operationTask in operationTasks)
        {
            operationTask.Cancel(cancelledAtUtc, actor);
        }

        return new MesAcceptedResponse("Accepted", workOrder.WorkOrderId, cancelledAtUtc);
    }
}

internal static class WorkOrderLifecycleCommandGuards
{
    public static async Task<WorkOrder> GetWorkOrderAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        return await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderIdValue == workOrderId,
            cancellationToken)
            ?? throw new KnownException($"未找到生产工单，WorkOrderId = {workOrderId}");
    }

    public static void ApplyTransition(WorkOrder workOrder, Action<WorkOrder> transition)
    {
        MesDomainRuleGuard.Enforce(() => transition(workOrder));
    }

    public static void EnsureActionAllowed(
        WorkOrder workOrder,
        string action,
        params string[] allowedStatuses)
    {
        if (!allowedStatuses.Contains(workOrder.Status, StringComparer.Ordinal))
        {
            throw new MesLifecycleConflictException(action, workOrder.Status);
        }
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
    string? IdempotencyKey = null,
    IReadOnlyCollection<string>? SourceDemandReferences = null) : ICommand<MesAcceptedResponse>;

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
        RuleForEach(x => x.SourceDemandReferences).NotEmpty().MaximumLength(100);
    }
}

public sealed class ConvertPlanToWorkOrderCommandHandler : ICommandHandler<ConvertPlanToWorkOrderCommand, MesAcceptedResponse>
{
    private const int ConvertedPlanPriority = 100;
    private readonly ApplicationDbContext dbContext;
    private readonly RuleScheduler scheduler;
    private readonly MesCodingService _codingService;
    private readonly IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider;
    private readonly IMesSkuAvailabilityScopeCoordinator skuAvailabilityScopeCoordinator;
    private readonly IMesRoutingSnapshotProvider? routingSnapshotProvider;

    public ConvertPlanToWorkOrderCommandHandler(
        ApplicationDbContext dbContext,
        RuleScheduler scheduler,
        MesCodingService codingService,
        IMesSkuAvailabilityScopeCoordinator skuAvailabilityScopeCoordinator,
        IMesMaterialRequirementSnapshotProvider materialSnapshotProvider,
        IMesRoutingSnapshotProvider routingSnapshotProvider)
    {
        this.dbContext = dbContext;
        this.scheduler = scheduler;
        _codingService = codingService;
        this.materialSnapshotProvider = materialSnapshotProvider;
        this.skuAvailabilityScopeCoordinator = skuAvailabilityScopeCoordinator;
        this.routingSnapshotProvider = routingSnapshotProvider;
    }

    internal ConvertPlanToWorkOrderCommandHandler(
        ApplicationDbContext dbContext,
        RuleScheduler scheduler,
        MesCodingService? codingService = null,
        IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider = null)
        : this(
            dbContext,
            scheduler,
            codingService,
            materialSnapshotProvider,
            new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext),
            null)
    {
    }

    internal ConvertPlanToWorkOrderCommandHandler(
        ApplicationDbContext dbContext,
        RuleScheduler scheduler,
        MesCodingService? codingService,
        IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider,
        IMesSkuAvailabilityScopeCoordinator skuAvailabilityScopeCoordinator,
        IMesRoutingSnapshotProvider? routingSnapshotProvider = null)
    {
        this.dbContext = dbContext;
        this.scheduler = scheduler;
        _codingService = codingService ?? new MesCodingService();
        this.materialSnapshotProvider = materialSnapshotProvider;
        this.skuAvailabilityScopeCoordinator = skuAvailabilityScopeCoordinator;
        this.routingSnapshotProvider = routingSnapshotProvider;
    }

    internal ConvertPlanToWorkOrderCommandHandler(ApplicationDbContext dbContext)
        : this(dbContext, new RuleScheduler())
    {
    }

    internal ConvertPlanToWorkOrderCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService)
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
            var replayedWorkOrderExists = dbContext.WorkOrders.Local.Any(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderIdValue == allocation.Code)
                || await dbContext.WorkOrders.AnyAsync(
                    x => x.OrganizationId == request.OrganizationId &&
                        x.EnvironmentId == request.EnvironmentId &&
                        x.WorkOrderIdValue == allocation.Code,
                    cancellationToken);
            if (replayedWorkOrderExists)
            {
                return new MesAcceptedResponse("Accepted", allocation.Code, request.RequestedAtUtc);
            }
        }

        await EnsureRoutingSnapshotPreconditionsAsync(request, cancellationToken);
        var routingSnapshot = await CaptureRoutingSnapshotAsync(request, allocation.Code, cancellationToken);
        return await skuAvailabilityScopeCoordinator.ExecuteAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuId,
            token => CreateWorkOrderAsync(
                request,
                allocation.Code,
                sourceSystem,
                sourceDocumentType,
                sourceDocumentId,
                routingSnapshot,
                token),
            cancellationToken);
    }

    private async Task EnsureRoutingSnapshotPreconditionsAsync(
        ConvertPlanToWorkOrderCommand request,
        CancellationToken cancellationToken)
    {
        await MesSkuAvailabilityGate.EnsureActiveAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuId,
            cancellationToken);
        await MesArchivedProductionVersionGuard.ThrowIfArchivedAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.ProductionVersionId,
            cancellationToken);
    }

    private async Task<MesRoutingSnapshotResult?> CaptureRoutingSnapshotAsync(
        ConvertPlanToWorkOrderCommand request,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.WorkCenterId))
        {
            return null;
        }

        var routingSnapshot = routingSnapshotProvider is null
            ? MesRoutingSnapshotResult.Missing(MesRoutingSnapshotSources.NotConfigured)
            : await routingSnapshotProvider.GetSnapshotAsync(
                new MesRoutingSnapshotRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    workOrderId,
                    request.SkuId,
                    request.ProductionVersionId,
                    request.PlannedQuantity,
                    request.RequestedAtUtc),
                cancellationToken);
        if (routingSnapshot.Status != MesRoutingSnapshotStatus.Captured || routingSnapshot.Operations.Count == 0)
        {
            throw new MesRoutingSnapshotMissingException(routingSnapshot.SourceSystem);
        }

        return routingSnapshot;
    }

    private async Task<MesAcceptedResponse> CreateWorkOrderAsync(
        ConvertPlanToWorkOrderCommand request,
        string workOrderId,
        string sourceSystem,
        string sourceDocumentType,
        string sourceDocumentId,
        MesRoutingSnapshotResult? routingSnapshot,
        CancellationToken cancellationToken)
    {
        await MesSkuAvailabilityGate.EnsureActiveAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuId,
            cancellationToken);
        await MesArchivedProductionVersionGuard.ThrowIfArchivedAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.ProductionVersionId,
            cancellationToken);
        var alreadyExists = await dbContext.WorkOrders.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == workOrderId,
            cancellationToken);
        if (alreadyExists)
        {
            throw new KnownException($"生产工单已存在，WorkOrderId = {workOrderId}");
        }

        var sourceReference = new SourcePlanReference(
            sourceSystem,
            sourceDocumentType,
            sourceDocumentId,
            request.SourceDemandReference,
            request.SourceDemandReferences);
        var workOrder = WorkOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            workOrderId,
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
                workOrderId,
                $"{workOrderId}-OP-10",
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
        else
        {
            foreach (var operation in routingSnapshot!.Operations.OrderBy(x => x.Sequence))
            {
                dbContext.OperationTasks.Add(OperationTask.Queue(
                    request.OrganizationId,
                    request.EnvironmentId,
                    workOrderId,
                    $"{workOrderId}-OP-{operation.Sequence}",
                    operation.Sequence,
                    operation.WorkCenterId,
                    operation.AlternativeWorkCenterIds,
                    request.RequestedAtUtc,
                    TimeSpan.FromMinutes(operation.StandardMinutes),
                    request.SkuId,
                    request.UomCode,
                    request.PlannedQuantity,
                    operation.RequiresQualityInspection,
                    operation.OperationCode));
            }
        }

        if (materialSnapshotProvider is not null)
        {
            var materialCapture = await MaterialReadinessGuards.EnsureRequirementSnapshotsAsync(
                dbContext,
                materialSnapshotProvider,
                workOrder,
                request.RequestedAtUtc,
                cancellationToken);
            if (materialCapture.IsMissing)
            {
                throw new KnownException("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照，无法确认物料齐套。");
            }
        }

        return new MesAcceptedResponse("Accepted", workOrderId, request.RequestedAtUtc);
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
        // 单位是物料主档的事实，不是界面常量。占位单位会一路带到集成事件转换处才炸（库存腿无法换算），
        // 那时已经是发布侧异常而非业务拒绝；在受理时就以业务错误回绝，让调用方能看懂并修正。
        if (string.Equals(uomCode, MaterialIssueRequest.UnspecifiedUomCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException("领料申请的单位不能是占位值，请按物料主档的基本计量单位提交。");
        }

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

public sealed class ConfirmLineSideMaterialReceiptCommandHandler(
    ApplicationDbContext dbContext,
    IMesMaterialSupplyLocationResolver supplyLocationResolver)
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

        if (materialRequest.Status is not MaterialIssueRequest.RequestedStatus and
            not MaterialIssueRequest.PartiallyReceivedStatus)
        {
            throw new MesLifecycleConflictException(
                "confirm-line-side-receipt",
                materialRequest.Status);
        }

        // 来源库位取库存实际持仓（配置候选库位 + 库存实时查询），目标库位取工位线边：
        // 过账位置不再由 MES 臆造，Inventory 也就不会再以 NEGATIVE_ON_HAND 全拒（#1322）。
        var postingQuantity = request.ReceivedQuantity ??
            materialRequest.RequestedQuantity - materialRequest.ReceivedQuantity;
        var locations = await supplyLocationResolver.ResolveAsync(
            new MesMaterialSupplyLocationRequest(
                materialRequest.OrganizationId,
                materialRequest.EnvironmentId,
                materialRequest.MaterialId,
                materialRequest.UomCode,
                request.MaterialLotId ?? materialRequest.MaterialLotId,
                postingQuantity),
            cancellationToken);

        MesDomainRuleGuard.Enforce(() =>
            materialRequest.ConfirmLineSideReceipt(
                locations,
                request.ReceivedAtUtc,
                request.ReceivedQuantity,
                request.MaterialLotId));
        return new MesAcceptedResponse("Accepted", materialRequest.RequestNo, request.ReceivedAtUtc);
    }
}

public sealed record ReturnLineSideMaterialCommand(
    string OrganizationId,
    string EnvironmentId,
    string RequestId,
    DateTimeOffset ReturnedAtUtc,
    decimal ReturnedQuantity) : ICommand<MesAcceptedResponse>;

public sealed class ReturnLineSideMaterialCommandValidator : AbstractValidator<ReturnLineSideMaterialCommand>
{
    public ReturnLineSideMaterialCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RequestId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReturnedQuantity).GreaterThan(0);
    }
}

public sealed class ReturnLineSideMaterialCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReturnLineSideMaterialCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(ReturnLineSideMaterialCommand request, CancellationToken cancellationToken)
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

        try
        {
            var consumedQuantity = await dbContext.ProductionReportMaterialConsumptions
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == materialRequest.OrganizationId &&
                    x.EnvironmentId == materialRequest.EnvironmentId &&
                    x.MaterialIssueRequestNo == materialRequest.RequestNo &&
                    x.MaterialId == materialRequest.MaterialId &&
                    x.MaterialLotId == materialRequest.MaterialLotId)
                .SumAsync(x => x.ConsumedQuantity, cancellationToken);
            materialRequest.ReturnLineSideMaterial(request.ReturnedAtUtc, request.ReturnedQuantity, consumedQuantity);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        return new MesAcceptedResponse("Accepted", materialRequest.RequestNo, request.ReturnedAtUtc);
    }
}

public sealed record AssignDispatchTaskCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string? AssignedUserId,
    string? DeviceAssetId,
    string? ShiftId,
    DateTimeOffset AssignedAtUtc,
    string Actor = "system:mes",
    string? AssignedUserName = null,
    string? TeamId = null,
    string? TeamName = null) : ICommand<MesAcceptedResponse>, IOperationTaskConcurrencyRetryCommand;

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

        var qualityIssues = await ReadinessReasonCodes.GetActiveQualityHoldIssuesAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            task.WorkOrderId,
            task.OperationTaskIdValue,
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
            request.AssignedAtUtc,
            cancellationToken);
        if (equipmentIssues.Count > 0)
        {
            throw new KnownException(string.Join("; ", equipmentIssues.Select(x => x.Code)));
        }

        MesDomainRuleGuard.Enforce(() =>
            task.Assign(
                request.AssignedUserId,
                request.DeviceAssetId,
                request.ShiftId,
                request.AssignedAtUtc,
                request.Actor,
                request.AssignedUserName,
                request.TeamId,
                request.TeamName));
        dbContext.Entry(task).Property(x => x.AssignedUserId).IsModified = true;
        dbContext.Entry(task).Property(x => x.AssignedUserName).IsModified = true;
        dbContext.Entry(task).Property(x => x.DeviceAssetId).IsModified = true;
        dbContext.Entry(task).Property(x => x.ShiftId).IsModified = true;
        dbContext.Entry(task).Property(x => x.TeamId).IsModified = true;
        dbContext.Entry(task).Property(x => x.TeamName).IsModified = true;
        dbContext.Entry(task).Property(x => x.AssignedAtUtc).IsModified = true;
        return new MesAcceptedResponse("Accepted", request.OperationTaskId, request.AssignedAtUtc);
    }
}

public sealed record ChangeOperationTaskStateCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationTaskId,
    string Action,
    DateTimeOffset ChangedAtUtc,
    string IdempotencyKey) : ICommand<MesOperationActionResponse>, IOperationTaskConcurrencyRetryCommand
{
    internal bool PersistsCallerIntentReceipt { get; private init; } = true;

    // Governed internal path for schedulers/tests that do not originate from a
    // frontline request. The same business facts derive the same validation key,
    // but this compatibility path preserves the pre-frontline behavior and does
    // not mint a caller-intent receipt. HTTP DTOs cannot select this constructor.
    public ChangeOperationTaskStateCommand(
        string OrganizationId,
        string EnvironmentId,
        string OperationTaskId,
        string Action,
        DateTimeOffset ChangedAtUtc)
        : this(
            OrganizationId,
            EnvironmentId,
            OperationTaskId,
            Action,
            ChangedAtUtc,
            $"internal:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{OrganizationId}|{EnvironmentId}|{OperationTaskId}|{Action}|{ChangedAtUtc:O}")))}")
    {
        PersistsCallerIntentReceipt = false;
    }
}

public sealed class ChangeOperationTaskStateCommandLock
    : NetCorePal.Extensions.Primitives.ICommandLock<ChangeOperationTaskStateCommand>
{
    public Task<NetCorePal.Extensions.Primitives.CommandLockSettings> GetLockKeysAsync(
        ChangeOperationTaskStateCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new NetCorePal.Extensions.Primitives.CommandLockSettings(
            $"business-mes:operation-task-action:{command.OrganizationId.Trim()}:{command.EnvironmentId.Trim()}:{command.OperationTaskId.Trim()}",
            30));
    }
}

public sealed class ChangeOperationTaskStateCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ChangeOperationTaskStateCommand, MesOperationActionResponse>
{
    private const string OperationActionRuleKey = "operation-task-action";

    public async Task<MesOperationActionResponse> Handle(ChangeOperationTaskStateCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken)
            ?? throw new KnownException($"未找到工序任务，OperationTaskId = {request.OperationTaskId}");

        var replay = await TryGetReplayAsync(request, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var requiredStatus = request.Action switch
        {
            "start" => OperationTaskLifecycleStatus.Queued,
            "pause" or "complete" => OperationTaskLifecycleStatus.InProgress,
            "resume" => OperationTaskLifecycleStatus.Paused,
            _ => (OperationTaskLifecycleStatus?)null,
        };
        if (requiredStatus is not null && task.Status != requiredStatus)
        {
            throw new MesLifecycleConflictException(request.Action, task.Status.ToString());
        }

        if (request.Action == "start")
        {
            var readiness = await new MesOperationTaskActionReadinessEvaluator(dbContext).EvaluateAsync(
                    task,
                    request.ChangedAtUtc,
                    cancellationToken);
            if (!readiness.AllowedActions.Contains("start", StringComparer.Ordinal))
            {
                throw new KnownException(MaterialReadinessGuards.DescribeForUser(readiness.BlockReasons));
            }

            var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderIdValue == task.WorkOrderId,
                cancellationToken)
                ?? throw new KnownException($"未找到生产工单，WorkOrderId = {task.WorkOrderId}");

            MesDomainRuleGuard.Enforce(() =>
            {
                task.Start(request.ChangedAtUtc);
                if (workOrder.Status is WorkOrder.ReleasedStatus or WorkOrder.HoldStatus)
                {
                    workOrder.Start(request.ChangedAtUtc);
                }
            });

            var startResult = new MesOperationActionResponse(
                task.OperationTaskIdValue,
                task.Status.ToString(),
                request.ChangedAtUtc);
            AddIdempotencyRecord(request, startResult);
            return startResult;
        }

        switch (request.Action)
        {
            case "pause":
                MesDomainRuleGuard.Enforce(() => task.Pause(request.ChangedAtUtc));
                break;
            case "resume":
                MesDomainRuleGuard.Enforce(() => task.Resume(request.ChangedAtUtc));
                break;
            case "complete":
                await EnsurePreviousOperationsCompletedAsync(dbContext, task, cancellationToken);
                MesDomainRuleGuard.Enforce(() => task.Complete(request.ChangedAtUtc));
                break;
            default:
                throw new KnownException($"不支持的工序动作：{request.Action}");
        }

        var result = new MesOperationActionResponse(
            task.OperationTaskIdValue,
            task.Status.ToString(),
            request.ChangedAtUtc);
        AddIdempotencyRecord(request, result);
        return result;
    }

    private async Task<MesOperationActionResponse?> TryGetReplayAsync(
        ChangeOperationTaskStateCommand request,
        CancellationToken cancellationToken)
    {
        if (!request.PersistsCallerIntentReceipt)
        {
            return null;
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        var existing = dbContext.CodeIdempotencyKeys.Local.FirstOrDefault(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RuleKey == OperationActionRuleKey &&
                x.IdempotencyKey == idempotencyKey)
            ?? await dbContext.CodeIdempotencyKeys.AsNoTracking().SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RuleKey == OperationActionRuleKey &&
                x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var fingerprint = Fingerprint(request);
        if (!string.Equals(existing.PayloadFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new MesIdempotencyConflictException();
        }

        var parts = existing.Code.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !DateTimeOffset.TryParseExact(
                parts[1],
                "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var changedAtUtc))
        {
            throw new KnownException(
                $"Stored idempotency receipt for MES operation-task action '{idempotencyKey}' is invalid.");
        }

        return new MesOperationActionResponse(
            request.OperationTaskId,
            parts[0],
            changedAtUtc);
    }

    private void AddIdempotencyRecord(
        ChangeOperationTaskStateCommand request,
        MesOperationActionResponse result)
    {
        if (!request.PersistsCallerIntentReceipt)
        {
            return;
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        dbContext.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            request.OrganizationId,
            request.EnvironmentId,
            OperationActionRuleKey,
            idempotencyKey,
            $"{result.Status}|{result.ChangedAtUtc:O}",
            Fingerprint(request),
            result.ChangedAtUtc));
    }

    private static string Fingerprint(ChangeOperationTaskStateCommand request) =>
        string.Join(
            '|',
            request.OperationTaskId.Trim().ToUpperInvariant(),
            request.Action.Trim().ToLowerInvariant());

    private static string NormalizeIdempotencyKey(string value) => value.Trim();

    internal static async Task EnsurePreviousOperationsCompletedAsync(
        ApplicationDbContext dbContext,
        OperationTask task,
        CancellationToken cancellationToken)
    {
        var blockingOperations = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == task.OrganizationId &&
                x.EnvironmentId == task.EnvironmentId &&
                x.WorkOrderId == task.WorkOrderId &&
                x.OperationSequence < task.OperationSequence &&
                x.Status != OperationTaskLifecycleStatus.Completed)
            .OrderBy(x => x.OperationSequence)
            .Select(x => x.OperationTaskIdValue)
            .ToArrayAsync(cancellationToken);
        if (blockingOperations.Length > 0)
        {
            // 这条会经分层透传直接上屏，而前端只原样透传 60 字以内的中文短消息（超了会被截断）——
            // 所以只说「哪几道工序还没完工」，不带 OperationTaskId = 这类内部字段名；
            // 前序太多时只点前三道，剩下的给个数（MAN-698 台账 #35 同批）。
            var named = blockingOperations.Take(3).ToArray();
            var more = blockingOperations.Length > named.Length
                ? $" 等 {blockingOperations.Length} 道"
                : string.Empty;
            throw new KnownException($"前序工序尚未完成：{string.Join('、', named)}{more}。");
        }
    }
}

internal static class MaterialReadinessGuards
{
    internal const string MissingRequirementSnapshotReason =
        "MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照，无法确认物料齐套。";

    /// <summary>
    /// 仍然算数的领料单状态:已发起、部分收料、已收料。
    /// 取消 / 退料中 / 预留失效的单子不代表仓库还在配货,聚合「应领」时必须排除,
    /// 否则齐套读面会把「其实没人在配」误标成「仓库配送中」。
    /// </summary>
    private static readonly string[] ActiveIssueRequestStatuses =
    [
        MaterialIssueRequest.RequestedStatus,
        MaterialIssueRequest.PartiallyReceivedStatus,
        // 收料已提交、库存过账在途:仓库仍在配货,「应领」要算;但「已收」只认过账成功的量。
        MaterialIssueRequest.ReceiptPostingStatus,
        MaterialIssueRequest.ReceivedStatus
    ];

    public static bool IsActiveIssueRequestStatus(string? status)
    {
        return !string.IsNullOrWhiteSpace(status)
            && ActiveIssueRequestStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 缺料阻塞原因在 **MES 服务内**的唯一措辞：<c>MATERIAL_SHORTAGE: 物料 X（批次 Y）缺口 N</c>，
    /// 与 <see cref="MissingRequirementSnapshotReason"/> 同一形态（<c>英文码: 中文说明</c>）——
    /// 前端按冒号前的码取标签与下一步动作，冒号后的中文原样作为明细上屏。
    /// 本服务内新增缺料产出点一律走这里（曾经三处各写一套、其中两处直出英文生码
    /// 「物料编码 + shortage + 数量」，界面上既读不懂又被徽标截断；MAN-698 台账 #35）。
    ///
    /// ⚠️ 这个形态是**跨服务约定**，但实现有意重复三份：本处、Scheduling 的
    /// <c>SchedulingMaterialReasonText</c>、前端的 <c>describeMesReadinessReason</c>。
    /// 服务边界不共享库、前端更不可能引用后端代码，所以**共享的是断言不是代码**：
    /// 本处与 Scheduling 侧各有一条逐字一致的格式用例互相钉住，改措辞两边一起红。
    /// </summary>
    public static string FormatShortageReason(string materialId, string? materialLotId, decimal shortage)
    {
        var lot = string.IsNullOrWhiteSpace(materialLotId) ? string.Empty : $"，批次 {materialLotId}";
        return $"MATERIAL_SHORTAGE: 物料 {materialId}{lot} 缺口 {shortage:0.######}";
    }

    /// <summary>
    /// 把阻塞原因串成**给用户看的一句话**：读面保留 <c>CODE: 中文</c>（前端按码取标签与下一步动作），
    /// 但写操作被拒时的 <see cref="KnownException"/> 文案要去掉英文码——它经分层透传直接上屏，
    /// 反馈规范禁止界面出现英文错误码（`frontend/DESIGN/patterns/feedback-and-notifications.md`）。
    /// </summary>
    public static string DescribeForUser(IEnumerable<string> reasons)
    {
        return string.Join("；", reasons.Select(StripReasonCode).Where(x => x.Length > 0));
    }

    private static string StripReasonCode(string reason)
    {
        var separator = reason.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return reason.Trim();
        }

        var code = reason[..separator];
        // 只剥「全大写下划线」形态的码，别把中文说明里的冒号误当分隔符。
        return code.All(x => char.IsAsciiLetterUpper(x) || char.IsAsciiDigit(x) || x == '_')
            ? reason[(separator + 1)..].Trim()
            : reason.Trim();
    }

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
            workOrder.RecordMaterialRequirementSnapshot(
                WorkOrder.MaterialRequirementSnapshotCapturedStatus,
                capturedAtUtc);
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
            workOrder.RecordMaterialRequirementSnapshot(
                WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
                capturedAtUtc);
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

        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotCapturedStatus,
            capturedAtUtc);
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
            .Select(x => FormatShortageReason(x.MaterialId, x.MaterialLotId, x.Shortage))
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

    public readonly record struct MaterialRequirementCaptureOutcome(bool NoRequirements, bool IsMissing)
    {
        public static MaterialRequirementCaptureOutcome Existing { get; } = new(false, false);

        public static MaterialRequirementCaptureOutcome Captured { get; } = new(false, false);

        public static MaterialRequirementCaptureOutcome Missing { get; } = new(false, true);

        public static MaterialRequirementCaptureOutcome NoRequirementsFound { get; } = new(true, false);
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
        string? operationTaskId,
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

        var issues = new List<ReadinessBlockingIssue>();
        if (string.IsNullOrWhiteSpace(productionVersionId))
        {
            issues.Add(
                new ReadinessBlockingIssue(
                    MesReadinessReasonCodes.QualityPlanMissing,
                    "Quality",
                    "InspectionPlan",
                    workOrderId,
                    "工单缺少已发布生产版本或检验方案。"));
        }

        issues.AddRange(await GetActiveQualityHoldIssuesAsync(
            dbContext,
            organizationId,
            environmentId,
            workOrderId,
            operationTaskId,
            cancellationToken));

        return issues;
    }

    public static async Task<IReadOnlyCollection<ReadinessBlockingIssue>> GetActiveQualityHoldIssuesAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        string? operationTaskId,
        CancellationToken cancellationToken)
    {
        var activeHolds = await dbContext.QualityHoldContexts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderId == workOrderId &&
                (operationTaskId == null || x.OperationTaskId == null || x.OperationTaskId == operationTaskId) &&
                x.Active)
            .OrderByDescending(x => x.RecordedAtUtc)
            .ToArrayAsync(cancellationToken);
        return activeHolds.Select(x => new ReadinessBlockingIssue(
            MesReadinessReasonCodes.QualityHoldActive,
            "Quality",
            "InspectionRecord",
            x.InspectionRecordId,
            string.IsNullOrWhiteSpace(x.DispositionReason)
                ? "工单存在有效质量保留，无法放行或开工。"
                : $"工单存在有效质量保留，无法放行或开工：{x.DispositionReason}"))
            .ToArray();
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
        // x.Id 是强类型 GuidId：谓词里 x.Id.Id.ToString() 无法被 EF 翻译（真机 500）。
        // 先按业务单号命中；只有请求确实是 Guid 时才用先物化好的强类型 Id 直接比较（可翻译）。
        var downtime = await dbContext.WorkCenterUnavailabilities.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.DowntimeEventNo == request.DowntimeEventId,
            cancellationToken);
        if (downtime is null && Guid.TryParse(request.DowntimeEventId, out var downtimeEventGuid))
        {
            var downtimeEventId = new WorkCenterUnavailabilityId(downtimeEventGuid);
            downtime = await dbContext.WorkCenterUnavailabilities.SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.Id == downtimeEventId,
                cancellationToken);
        }

        downtime = downtime
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
    string? IdempotencyKey = null,
    string? TeamName = null) : ICommand<MesAcceptedResponse>;

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
            request.HandoverAtUtc,
            request.TeamName);
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
