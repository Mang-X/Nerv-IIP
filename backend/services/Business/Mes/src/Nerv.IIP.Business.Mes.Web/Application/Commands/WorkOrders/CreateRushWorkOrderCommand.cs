using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.MasterData;

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

public sealed record CreateRushWorkOrderResponse(string WorkOrderId);

public sealed class CreateRushWorkOrderCommandHandler
    : ICommandHandler<CreateRushWorkOrderCommand, CreateRushWorkOrderResponse>
{
    private const int RushPriority = 1000;
    private readonly IMesPlanningStore store;
    private readonly MesCodingService _codingService;
    private readonly ApplicationDbContext? dbContext;
    private readonly IMesSkuAvailabilityScopeCoordinator? skuAvailabilityScopeCoordinator;
    private readonly IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider;

    public CreateRushWorkOrderCommandHandler(
        IMesPlanningStore store,
        MesCodingService codingService,
        ApplicationDbContext dbContext,
        IMesSkuAvailabilityScopeCoordinator skuAvailabilityScopeCoordinator,
        IMesMaterialRequirementSnapshotProvider materialSnapshotProvider)
        : this(store, codingService, dbContext, skuAvailabilityScopeCoordinator, materialSnapshotProvider, isTestConstruction: false)
    {
    }

    internal CreateRushWorkOrderCommandHandler(
        IMesPlanningStore store,
        MesCodingService? codingService = null,
        ApplicationDbContext? dbContext = null,
        IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider = null)
        : this(
            store,
            codingService ?? new MesCodingService(),
            dbContext,
            dbContext is null ? null : new PostgreSqlMesSkuAvailabilityScopeCoordinator(dbContext),
            materialSnapshotProvider,
            isTestConstruction: true)
    {
    }

    private CreateRushWorkOrderCommandHandler(
        IMesPlanningStore store,
        MesCodingService codingService,
        ApplicationDbContext? dbContext,
        IMesSkuAvailabilityScopeCoordinator? skuAvailabilityScopeCoordinator,
        IMesMaterialRequirementSnapshotProvider? materialSnapshotProvider,
        bool isTestConstruction)
    {
        _ = isTestConstruction;
        this.store = store;
        _codingService = codingService;
        this.dbContext = dbContext;
        this.skuAvailabilityScopeCoordinator = skuAvailabilityScopeCoordinator;
        this.materialSnapshotProvider = materialSnapshotProvider;
    }


    public async Task<CreateRushWorkOrderResponse> Handle(CreateRushWorkOrderCommand request, CancellationToken cancellationToken)
    {
        // 急单与计划转工单同样要按生产版本出物料清单、冻结齐套需求；缺了版本，下达时既没有
        // 齐套快照也过不了放行门禁（#3858）。在建单这一步就说清楚，别让用户到下达时才撞墙。
        if (string.IsNullOrWhiteSpace(request.ProductionVersionId))
        {
            throw new KnownException("急单必须选择生产版本：请先为该物料选择当前有效的生产版本。");
        }

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
                return new CreateRushWorkOrderResponse(allocation.Code);
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

        store.AddWorkOrder(new PlannedWorkOrder(
            request.OrganizationId,
            request.EnvironmentId,
            workOrderId,
            request.SkuId,
            request.ProductionVersionId,
            request.Quantity,
            RushPriority,
            request.DueUtc,
            IsRush: true));
        store.AddOperationTask(new PlannedOperationTask(
            workOrderId,
            operationTaskId,
            OperationTaskStatus.Queued,
            request.OperationSequence,
            request.WorkCenterId,
            [],
            request.RequestedAtUtc,
            request.Duration,
            // 加急工单的工序必须带工单真实 SKU：以前 PlannedOperationTask 没有 SKU 字段，
            // 落库时 OperationTask 回落成工单号，完工事件因此与 WorkOrderReleased 不同源（#3112）。
            request.SkuId,
            null,
            null,
            request.OrganizationId,
            request.EnvironmentId));

        if (dbContext is not null && materialSnapshotProvider is not null)
        {
            // 与计划转工单同一处实现：建单时按生产版本冻结齐套需求，下达门禁与齐套读面都读这份快照（#3858）。
            var workOrder = dbContext.WorkOrders.Local.Single(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == workOrderId);
            var materialCapture = await MaterialReadinessGuards.EnsureRequirementSnapshotsAsync(
                dbContext,
                materialSnapshotProvider,
                workOrder,
                request.RequestedAtUtc,
                cancellationToken);
            if (materialCapture.IsMissing)
            {
                throw new KnownException("无法按所选生产版本生成齐套需求，急单未创建。请确认该版本当前有效且制造物料清单已发布。");
            }
        }

        return new CreateRushWorkOrderResponse(workOrderId);
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
