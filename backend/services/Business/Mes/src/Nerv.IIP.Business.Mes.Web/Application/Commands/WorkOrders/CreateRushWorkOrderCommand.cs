using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.MasterData;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record CreateRushWorkOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    DateTimeOffset DueUtc,
    string WorkCenterId,
    string? OperationTaskId,
    int OperationSequence,
    TimeSpan Duration,
    DateTimeOffset RequestedAtUtc,
    string? IdempotencyKey = null) : ICommand<CreateRushWorkOrderResponse>;

public sealed record CreateRushWorkOrderResponse(
    string WorkOrderId,
    MesScheduleResult Schedule,
    IReadOnlyCollection<string> AffectedWorkOrderIds);

public sealed class CreateRushWorkOrderCommandHandler
    : ICommandHandler<CreateRushWorkOrderCommand, CreateRushWorkOrderResponse>
{
    private const int RushPriority = 1000;
    private readonly IMesPlanningStore store;
    private readonly RuleScheduler scheduler;
    private readonly MesCodingService _codingService;
    private readonly ApplicationDbContext? dbContext;
    private readonly IMesSkuAvailabilityScopeCoordinator? skuAvailabilityScopeCoordinator;

    public CreateRushWorkOrderCommandHandler(
        IMesPlanningStore store,
        RuleScheduler scheduler,
        MesCodingService codingService,
        ApplicationDbContext dbContext,
        IMesSkuAvailabilityScopeCoordinator skuAvailabilityScopeCoordinator)
        : this(store, scheduler, codingService, dbContext, skuAvailabilityScopeCoordinator, isTestConstruction: false)
    {
    }

    internal CreateRushWorkOrderCommandHandler(
        IMesPlanningStore store,
        RuleScheduler scheduler,
        MesCodingService? codingService = null,
        ApplicationDbContext? dbContext = null)
        : this(
            store,
            scheduler,
            codingService ?? new MesCodingService(),
            dbContext,
            dbContext is null ? null : new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext),
            isTestConstruction: true)
    {
    }

    private CreateRushWorkOrderCommandHandler(
        IMesPlanningStore store,
        RuleScheduler scheduler,
        MesCodingService codingService,
        ApplicationDbContext? dbContext,
        IMesSkuAvailabilityScopeCoordinator? skuAvailabilityScopeCoordinator,
        bool isTestConstruction)
    {
        _ = isTestConstruction;
        this.store = store;
        this.scheduler = scheduler;
        _codingService = codingService;
        this.dbContext = dbContext;
        this.skuAvailabilityScopeCoordinator = skuAvailabilityScopeCoordinator;
    }

    public async Task<CreateRushWorkOrderResponse> Handle(CreateRushWorkOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateWorkOrderIdAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.IdempotencyKey,
            WorkOrderPayloadFingerprint(request),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var replayedWorkOrderExists = await store.WorkOrderExistsAsync(
                request.OrganizationId,
                request.EnvironmentId,
                allocation.Code,
                cancellationToken);
            if (replayedWorkOrderExists)
            {
                return new CreateRushWorkOrderResponse(
                    allocation.Code,
                    new MesScheduleResult(0, RescheduleTrigger.RushOrder, request.RequestedAtUtc, [], []),
                    []);
            }
        }

        if (dbContext is not null && skuAvailabilityScopeCoordinator is not null)
        {
            return await skuAvailabilityScopeCoordinator.ExecuteAsync(
                request.OrganizationId,
                request.EnvironmentId,
                request.SkuId,
                token => CreateWorkOrderAsync(request, allocation.Code, token),
                cancellationToken);
        }

        return await CreateWorkOrderAsync(request, allocation.Code, cancellationToken);
    }

    private async Task<CreateRushWorkOrderResponse> CreateWorkOrderAsync(
        CreateRushWorkOrderCommand request,
        string workOrderId,
        CancellationToken cancellationToken)
    {
        if (dbContext is not null)
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

        var operationTaskId = string.IsNullOrWhiteSpace(request.OperationTaskId)
            ? $"{workOrderId}-OP-{request.OperationSequence}"
            : request.OperationTaskId.Trim();
        var baselinePlan = scheduler.Schedule(
            await store.GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
            await store.GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));

        store.AddWorkOrder(new PlannedWorkOrder(
            request.OrganizationId,
            request.EnvironmentId,
            workOrderId,
            request.SkuId,
            request.ProductionVersionId,
            request.Quantity,
            RushPriority,
            request.DueUtc));
        store.AddOperationTask(new PlannedOperationTask(
            workOrderId,
            operationTaskId,
            OperationTaskStatus.Queued,
            request.OperationSequence,
            request.WorkCenterId,
            [],
            request.RequestedAtUtc,
            request.Duration,
            null,
            null,
            request.OrganizationId,
            request.EnvironmentId));

        var plan = scheduler.Schedule(
            await store.GetScheduleOperationsAsync(request.OrganizationId, request.EnvironmentId, cancellationToken),
            await store.GetUnavailabilitiesAsync(request.OrganizationId, request.EnvironmentId, cancellationToken));
        var schedule = await store.AddScheduleResultAsync(
            RescheduleTrigger.RushOrder,
            request.RequestedAtUtc,
            plan,
            baselinePlan.Assignments,
            cancellationToken);
        return new CreateRushWorkOrderResponse(
            workOrderId,
            schedule,
            schedule.AffectedWorkOrderIds);
    }

    private static string WorkOrderPayloadFingerprint(CreateRushWorkOrderCommand request)
    {
        return string.Join('|',
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuId,
            request.ProductionVersionId,
            request.Quantity,
            request.DueUtc.ToUnixTimeMilliseconds(),
            request.WorkCenterId,
            request.OperationSequence,
            request.Duration.Ticks);
    }
}
