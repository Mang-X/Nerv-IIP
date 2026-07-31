using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// #1324 领料全链：控制台发起领料 → MES 集成事件转换 → WMS 生成出库单/拣货任务 → 出库单回写 MES。
/// 建链时曾在这里断过两次：创建根本不发事件（仓库永远收不到），以及界面写死占位单位导致
/// converter 抛异常（事件在发布侧就炸，WMS 同样收不到）。
/// </summary>
public sealed class MesWmsMaterialIssueChainAcceptanceTests
{
    [Fact]
    public async Task Console_material_issue_reaches_wms_as_outbound_and_picking_work_and_reports_the_document_back()
    {
        await using var mesDb = CreateMesContext();
        await using var wmsDb = CreateWmsContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync(CancellationToken.None);
        var requestedAtUtc = DateTimeOffset.Parse("2026-07-31T08:00:00Z");

        // 1) 控制台提交（单位取自物料主档，不是占位值）
        var accepted = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001", "env-dev", "WO-1324", "OP-10", "MAT-OIL", "L", 6m, requestedAtUtc, "issue-1324"),
            CancellationToken.None);
        await mesDb.SaveChangesAsync(CancellationToken.None);
        var issueRequest = await mesDb.MaterialIssueRequests.SingleAsync(CancellationToken.None);
        Assert.Equal(accepted.ReferenceId, issueRequest.RequestNo);

        // 2) 领域事件 → 集成事件（占位单位会在这里抛异常，本用例守住它不会）
        var creation = Assert.Single(issueRequest.GetDomainEvents().OfType<MaterialIssueRequestCreatedDomainEvent>());
        var requestedEvent = new MaterialIssueRequestCreatedIntegrationEventConverter().Convert(creation);
        Assert.Equal("L", requestedEvent.Payload.UomCode);

        // 3) WMS 消费：出库单 + 拣货任务
        var wmsHandler = new MesMaterialIssueRequestedIntegrationEventHandler(
            wmsDb,
            new WmsCommandSender(wmsDb),
            new InMemoryIntegrationEventDeadLetterStore());
        await wmsHandler.HandleAsync(
            requestedEvent with { Payload = requestedEvent.Payload with { SiteCode = "SITE-001" } },
            CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        var outbound = await wmsDb.OutboundOrders.Include(x => x.Lines).SingleAsync(CancellationToken.None);
        Assert.Equal($"MI-{issueRequest.RequestNo}", outbound.OutboundOrderNo);
        Assert.Equal(issueRequest.RequestNo, outbound.SourceDocumentId);
        Assert.Equal("L", Assert.Single(outbound.Lines).UomCode);
        var pickingTask = await wmsDb.WarehouseTasks.SingleAsync(CancellationToken.None);
        Assert.Equal(WarehouseTaskType.Picking, pickingTask.TaskType);
        Assert.Equal(6m, pickingTask.PlannedQuantity);

        // 4) 出库单回写 MES
        var prepared = Assert.Single(outbound.GetDomainEvents().OfType<Nerv.IIP.Business.Wms.Domain.DomainEvents.MaterialIssueOutboundPreparedDomainEvent>());
        var preparedEvent = new MaterialIssueOutboundPreparedIntegrationEventConverter().Convert(prepared);
        var mesLinkHandler = new WmsMaterialIssueOutboundPreparedIntegrationEventHandlerForLinkOutbound(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());
        await mesLinkHandler.HandleAsync(preparedEvent, CancellationToken.None);

        var linked = await mesDb.MaterialIssueRequests.SingleAsync(CancellationToken.None);
        Assert.Equal(outbound.OutboundOrderNo, linked.WmsRequestId);
        Assert.Equal(pickingTask.TaskNo, linked.WmsPickingTaskNo);
    }

    [Fact]
    public async Task Placeholder_uom_is_refused_as_a_business_error_instead_of_exploding_in_the_converter()
    {
        await using var mesDb = CreateMesContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(
            () => new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
                new CreateMaterialIssueRequestCommand(
                    "org-001",
                    "env-dev",
                    "WO-1324",
                    "OP-10",
                    "MAT-OIL",
                    MaterialIssueRequest.UnspecifiedUomCode,
                    6m,
                    DateTimeOffset.Parse("2026-07-31T08:00:00Z"),
                    "issue-1324-placeholder"),
                CancellationToken.None));

        Assert.Contains("单位不能是占位值", exception.Message, StringComparison.Ordinal);
        Assert.False(await mesDb.MaterialIssueRequests.AnyAsync(CancellationToken.None));
    }

    private static void SeedMesWorkOrder(MesDbContext mesDb)
    {
        var now = DateTimeOffset.Parse("2026-07-31T07:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-1324", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8));
        workOrder.MarkReleased();
        workOrder.Start(now);
        mesDb.WorkOrders.Add(workOrder);
        var operationTask = OperationTask.Create(
            "org-001", "env-dev", "WO-1324", "OP-10", OperationTaskLifecycleStatus.Queued, 10, "WC-10", [], now,
            TimeSpan.FromMinutes(30), null, null);
        operationTask.Start(now);
        mesDb.OperationTasks.Add(operationTask);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static MesDbContext CreateMesContext(string? databaseName = null) =>
        new(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseInMemoryDatabase(databaseName ?? $"mes-material-issue-chain-{Guid.CreateVersion7():N}")
                .Options,
            new NoopMediator());

    private static WmsDbContext CreateWmsContext() =>
        new(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase($"wms-material-issue-chain-{Guid.CreateVersion7():N}")
                .Options,
            new NoopMediator());

    private sealed class WmsCommandSender(WmsDbContext dbContext) : ISender
    {
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("This test sender only supports command requests with responses.");

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PrepareMesMaterialIssueOutboundCommand command)
            {
                var result = await new PrepareMesMaterialIssueOutboundCommandHandler(dbContext).Handle(command, cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request?.GetType().FullName}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test sender only supports typed command requests.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test sender does not support streams.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This test sender does not support streams.");
    }
}
