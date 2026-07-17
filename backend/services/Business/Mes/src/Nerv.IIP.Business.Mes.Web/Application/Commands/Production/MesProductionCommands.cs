using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

public sealed record ProductionReportCommandResult(ProductionReportId Id, string ReportNo);

public sealed record ReverseProductionReportCommandResult(ProductionReportId Id, string ReportNo, string OriginalReportNo);

public sealed record FinishedGoodsReceiptRequestCommandResult(FinishedGoodsReceiptRequestId Id, string RequestNo);

public sealed record ConsumedMaterialLotInput(
    string MaterialId,
    string MaterialLotId,
    decimal ConsumedQuantity,
    string MaterialIssueRequestNo);

public sealed record RecordProductionReportCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    bool CompletesOperation,
    DateTimeOffset ReportedAtUtc,
    string? IdempotencyKey = null,
    IReadOnlyCollection<ConsumedMaterialLotInput>? ConsumedMaterialLots = null,
    decimal ReworkQuantity = 0m,
    string? ScrapReasonCode = null,
    string? DefectRecordNo = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    string Source = "manual") : ICommand<ProductionReportCommandResult>, IOperationTaskConcurrencyRetryCommand;

public sealed class RecordProductionReportCommandValidator : AbstractValidator<RecordProductionReportCommand>
{
    public RecordProductionReportCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationTaskId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.GoodQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ScrapQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReworkQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x).Must(x => x.GoodQuantity + x.ScrapQuantity + x.ReworkQuantity > 0)
            .WithMessage("At least one reported quantity must be positive.");
        RuleFor(x => x.Source).NotEmpty().MaximumLength(50).Must(ProductionReport.IsSupportedSource)
            .WithMessage("Production report source must be manual or telemetry.");
        RuleForEach(x => x.ConsumedMaterialLots).ChildRules(lot =>
        {
            lot.RuleFor(x => x.MaterialId).NotEmpty().MaximumLength(100);
            lot.RuleFor(x => x.MaterialLotId).NotEmpty().MaximumLength(100);
            lot.RuleFor(x => x.ConsumedQuantity).GreaterThan(0);
            lot.RuleFor(x => x.MaterialIssueRequestNo).NotEmpty().MaximumLength(100);
        });
    }
}

public sealed class RecordProductionReportCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<RecordProductionReportCommand, ProductionReportCommandResult>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<ProductionReportCommandResult> Handle(RecordProductionReportCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "production-report",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(
                request.WorkOrderId,
                request.OperationTaskId,
                request.GoodQuantity,
                request.ScrapQuantity,
                request.ReworkQuantity,
                request.CompletesOperation,
                request.ReportedAtUtc,
                request.ScrapReasonCode,
                request.DefectRecordNo,
                request.ProducedLotNo,
                request.SerialNo,
                request.Source,
                ConsumedMaterialLotsFingerprint(request.ConsumedMaterialLots)),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var existing = await dbContext.ProductionReports.SingleAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.ReportNo == allocation.Code,
                cancellationToken);
            return new ProductionReportCommandResult(existing.Id, existing.ReportNo);
        }

        var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken)
            ?? throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        var operationTask = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                x.OperationTaskIdValue == request.OperationTaskId,
            cancellationToken);
        if (operationTask is null)
        {
            throw new KnownException($"报工工序任务不存在或不属于当前工单，WorkOrderId = {request.WorkOrderId}, OperationTaskId = {request.OperationTaskId}");
        }

        var outputOperationSequence = await dbContext.OperationTasks
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .MaxAsync(x => x.OperationSequence, cancellationToken);
        var isOutputOperation = operationTask.OperationSequence == outputOperationSequence;
        var consumedMaterialLots = request.ConsumedMaterialLots ?? [];
        if (request.ScrapQuantity > 0m && consumedMaterialLots.Count == 0)
        {
            throw new KnownException("报废报工必须引用耗料批次，以触发在制物料报废核销。");
        }

        var producedLotNo = request.ProducedLotNo;
        if (isOutputOperation && request.GoodQuantity > 0m && string.IsNullOrWhiteSpace(producedLotNo))
        {
            producedLotNo = $"{request.WorkOrderId}-{request.OperationTaskId}-{allocation.Code}";
        }

        var report = ProductionReport.Record(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.WorkOrderId,
            request.OperationTaskId,
            request.GoodQuantity,
            request.ScrapQuantity,
            request.CompletesOperation,
            request.ReportedAtUtc,
            request.ReworkQuantity,
            request.ScrapReasonCode,
            request.DefectRecordNo,
            producedLotNo,
            request.SerialNo,
            ProductionReportOeeProjectionFactory.Create(operationTask),
            request.Source,
            consumedMaterialLots.Count);

        var duplicateLot = consumedMaterialLots
            .GroupBy(x => $"{x.MaterialId.ToUpperInvariant()}|{x.MaterialLotId.ToUpperInvariant()}", StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateLot is not null)
        {
            var lot = duplicateLot.First();
            throw new KnownException($"报工耗料批次重复，MaterialId = {lot.MaterialId}, MaterialLotId = {lot.MaterialLotId}");
        }

        if (isOutputOperation && request.GoodQuantity > 0m)
        {
            var outputLotExists = await dbContext.OutputLotGenealogies
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.OrganizationId == request.OrganizationId &&
                        x.EnvironmentId == request.EnvironmentId &&
                        x.ProducedLotNo == producedLotNo,
                    cancellationToken);
            if (outputLotExists)
            {
                throw new KnownException($"产出批次已存在，ProducedLotNo = {producedLotNo}");
            }
        }

        var materialConsumptions = new List<ProductionReportMaterialConsumption>();
        foreach (var lot in consumedMaterialLots)
        {
            if (string.IsNullOrWhiteSpace(lot.MaterialIssueRequestNo))
            {
                throw new KnownException($"报工耗料必须引用线边领料申请，MaterialLotId = {lot.MaterialLotId}");
            }

            var materialIssueRequest = await dbContext.MaterialIssueRequests
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.RequestNo == lot.MaterialIssueRequestNo &&
                    x.WorkOrderId == request.WorkOrderId &&
                    (x.OperationTaskId == null || x.OperationTaskId == request.OperationTaskId) &&
                    x.MaterialId == lot.MaterialId &&
                    x.MaterialLotId == lot.MaterialLotId)
                .Select(x => new { x.RequestNo, x.UomCode, x.ReceivedQuantity })
                .SingleOrDefaultAsync(cancellationToken);
            if (materialIssueRequest is null)
            {
                throw new KnownException($"报工引用的线边物料批次未接收、数量不足，或不属于当前工单或工序，MaterialLotId = {lot.MaterialLotId}");
            }

            var previouslyConsumedQuantity = await dbContext.ProductionReportMaterialConsumptions
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.MaterialIssueRequestNo == materialIssueRequest.RequestNo &&
                    x.MaterialId == lot.MaterialId &&
                    x.MaterialLotId == lot.MaterialLotId)
                .SumAsync(x => x.ConsumedQuantity, cancellationToken);
            if (previouslyConsumedQuantity + lot.ConsumedQuantity > materialIssueRequest.ReceivedQuantity)
            {
                throw new KnownException($"累计耗料超过线边接收数量，MaterialLotId = {lot.MaterialLotId}");
            }

            materialConsumptions.Add(ProductionReportMaterialConsumption.Record(
                request.OrganizationId,
                request.EnvironmentId,
                report.ReportNo,
                request.WorkOrderId,
                request.OperationTaskId,
                lot.MaterialId,
                lot.MaterialLotId,
                materialIssueRequest.UomCode,
                lot.ConsumedQuantity,
                lot.MaterialIssueRequestNo));
        }

        workOrder.RegisterCostReport(consumedMaterialLots.Count);
        if (isOutputOperation)
        {
            MesDomainRuleGuard.Enforce(() =>
                workOrder.RecordProductionProgress(request.GoodQuantity, request.ScrapQuantity, request.ReportedAtUtc));
        }

        if (request.CompletesOperation)
        {
            await ChangeOperationTaskStateCommandHandler.EnsurePreviousOperationsCompletedAsync(
                dbContext,
                operationTask,
                cancellationToken);
            MesDomainRuleGuard.Enforce(() => operationTask.Complete(request.ReportedAtUtc));
        }

        dbContext.ProductionReports.Add(report);
        dbContext.ProductionReportMaterialConsumptions.AddRange(materialConsumptions);
        if (isOutputOperation && request.GoodQuantity > 0m)
        {
            dbContext.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId,
                request.OperationTaskId,
                report.ReportNo,
                report.ProducedLotNo!,
                report.SerialNo,
                request.GoodQuantity,
                request.ReportedAtUtc));
        }

        await Task.CompletedTask;
        return new ProductionReportCommandResult(report.Id, report.ReportNo);
    }

    private static string ConsumedMaterialLotsFingerprint(IReadOnlyCollection<ConsumedMaterialLotInput>? lots)
    {
        return string.Join(
            ";",
            (lots ?? [])
                .Select(x => $"{x.MaterialId.Trim().ToUpperInvariant()}|{x.MaterialLotId.Trim().ToUpperInvariant()}|{x.ConsumedQuantity:0.######}|{x.MaterialIssueRequestNo.Trim().ToUpperInvariant()}")
                .Order(StringComparer.Ordinal));
    }
}

public sealed record ReverseProductionReportCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReportNo,
    string Reason,
    DateTimeOffset ReversedAtUtc,
    string ActorRef,
    string? IdempotencyKey = null) : ICommand<ReverseProductionReportCommandResult>, IOperationTaskConcurrencyRetryCommand;

public sealed class ReverseProductionReportCommandValidator : AbstractValidator<ReverseProductionReportCommand>
{
    public ReverseProductionReportCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReportNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ActorRef).NotEmpty().MaximumLength(100);
    }
}

public sealed class ReverseProductionReportCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<ReverseProductionReportCommand, ReverseProductionReportCommandResult>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<ReverseProductionReportCommandResult> Handle(ReverseProductionReportCommand request, CancellationToken cancellationToken)
    {
        var normalizedActorRef = request.ActorRef.Trim();
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "production-report",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(request.ReportNo, request.Reason, request.ReversedAtUtc, normalizedActorRef),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var existing = await dbContext.ProductionReports.SingleAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.ReportNo == allocation.Code,
                cancellationToken);
            return new ReverseProductionReportCommandResult(existing.Id, existing.ReportNo, existing.ReversedReportNo ?? request.ReportNo);
        }

        var original = await dbContext.ProductionReports.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.ReportNo == request.ReportNo,
            cancellationToken)
            ?? throw new KnownException($"未找到原报工，ReportNo = {request.ReportNo}");
        if (original.IsReversal)
        {
            throw new KnownException($"冲销报工不能再次冲销，ReportNo = {request.ReportNo}");
        }

        var alreadyReversed = await dbContext.ProductionReports.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.ReversedReportNo == original.ReportNo,
            cancellationToken);
        if (alreadyReversed)
        {
            throw new KnownException($"原报工已冲销，ReportNo = {request.ReportNo}");
        }

        var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == original.WorkOrderId,
            cancellationToken)
            ?? throw new KnownException($"未找到生产工单，WorkOrderId = {original.WorkOrderId}");
        if (workOrder.Status == WorkOrder.ClosedStatus)
        {
            throw new KnownException($"已关闭工单不允许冲销报工，WorkOrderId = {original.WorkOrderId}");
        }

        var operationTask = await dbContext.OperationTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == original.WorkOrderId &&
                x.OperationTaskIdValue == original.OperationTaskId,
            cancellationToken)
            ?? throw new KnownException($"报工工序任务不存在或不属于当前工单，WorkOrderId = {original.WorkOrderId}, OperationTaskId = {original.OperationTaskId}");

        if (!string.IsNullOrWhiteSpace(original.ProducedLotNo))
        {
            var producedLotReceiptRequests = await dbContext.FinishedGoodsReceiptRequests
                .Where(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderId == original.WorkOrderId &&
                    x.ProducedLotNo == original.ProducedLotNo)
                .ToArrayAsync(cancellationToken);
            if (producedLotReceiptRequests.Any(x => x.Status == FinishedGoodsReceiptRequest.PostedStatus))
            {
                throw new KnownException($"产出批次已完成库存入库，不能冲销原报工，ProducedLotNo = {original.ProducedLotNo}");
            }

            foreach (var receiptRequest in producedLotReceiptRequests)
            {
                receiptRequest.Cancel();
            }
        }

        var outputOperationSequence = await dbContext.OperationTasks
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == original.WorkOrderId)
            .MaxAsync(x => x.OperationSequence, cancellationToken);
        var isOutputOperation = operationTask.OperationSequence == outputOperationSequence;
        var progressQuantity = Math.Abs(original.GoodQuantity) + Math.Abs(original.ScrapQuantity);
        if (isOutputOperation && progressQuantity > 0m)
        {
            MesDomainRuleGuard.Enforce(() => workOrder.ReverseProductionProgress(
                Math.Abs(original.GoodQuantity),
                Math.Abs(original.ScrapQuantity),
                request.ReversedAtUtc));
        }

        if (original.CompletesOperation)
        {
            operationTask.ReopenAfterReportReversal();
        }

        var reversal = ProductionReport.Reverse(
            original,
            allocation.Code,
            request.ReversedAtUtc,
            request.Reason,
            normalizedActorRef);
        var originalConsumptions = await dbContext.ProductionReportMaterialConsumptions
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.ReportNo == original.ReportNo)
            .ToArrayAsync(cancellationToken);
        var reversalConsumptions = originalConsumptions
            .Select(x => ProductionReportMaterialConsumption.Reverse(x, reversal.ReportNo))
            .ToArray();

        var originalOutputLots = await dbContext.OutputLotGenealogies
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.ReportNo == original.ReportNo)
            .ToArrayAsync(cancellationToken);

        dbContext.ProductionReports.Add(reversal);
        dbContext.ProductionReportMaterialConsumptions.AddRange(reversalConsumptions);
        dbContext.OutputLotGenealogies.RemoveRange(originalOutputLots);
        return new ReverseProductionReportCommandResult(reversal.Id, reversal.ReportNo, original.ReportNo);
    }
}

internal static class ProductionReportOeeProjectionFactory
{
    public static ProductionReportOeeProjection Create(OperationTask operationTask)
    {
        ArgumentNullException.ThrowIfNull(operationTask);
        var durationHours = decimal.Divide(operationTask.DurationTicks, TimeSpan.TicksPerHour);
        decimal? theoreticalRatePerHour = operationTask.PlannedQuantity > 0m && durationHours > 0m
            ? decimal.Divide(operationTask.PlannedQuantity, durationHours)
            : null;
        return new ProductionReportOeeProjection(
            operationTask.WorkCenterId,
            operationTask.DeviceAssetId,
            operationTask.UomCode,
            theoreticalRatePerHour);
    }
}

public sealed record CreateFinishedGoodsReceiptRequestCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc,
    decimal? UnitCost,
    string? IdempotencyKey = null,
    string? ProducedLotNo = null,
    string? SerialNo = null,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null) : ICommand<FinishedGoodsReceiptRequestCommandResult>;

public sealed record RetryFinishedGoodsReceiptInventoryPostingCommand(
    string OrganizationId,
    string EnvironmentId,
    string RequestNo,
    string IdempotencyKey) : ICommand<FinishedGoodsReceiptRequestCommandResult>;

public sealed class CreateFinishedGoodsReceiptRequestCommandValidator : AbstractValidator<CreateFinishedGoodsReceiptRequestCommand>
{
    public CreateFinishedGoodsReceiptRequestCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(30);
        // UnitCost is optional by design — FinishedGoodsReceiptRequest.Create stores null as-is and only guards
        // positivity when a value is provided. Validate the same way (positive only when present) so the API
        // does not reject a cost-less receipt the domain accepts.
        RuleFor(x => x.UnitCost).GreaterThan(0).When(x => x.UnitCost.HasValue);
    }
}

public sealed class CreateFinishedGoodsReceiptRequestCommandHandler(ApplicationDbContext dbContext, MesCodingService? codingService = null)
    : ICommandHandler<CreateFinishedGoodsReceiptRequestCommand, FinishedGoodsReceiptRequestCommandResult>
{
    private readonly MesCodingService _codingService = codingService ?? new MesCodingService();

    public async Task<FinishedGoodsReceiptRequestCommandResult> Handle(CreateFinishedGoodsReceiptRequestCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "finished-goods-receipt-request",
            null,
            request.IdempotencyKey,
            MesCodingService.Fingerprint(request.WorkOrderId, request.SkuId, request.Quantity, request.UomCode, request.RequestedAtUtc, request.UnitCost, request.ProducedLotNo, request.SerialNo, request.ProductionDate, request.ExpiryDate),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var existing = await dbContext.FinishedGoodsReceiptRequests.SingleAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.RequestNo == allocation.Code,
                cancellationToken);
            return new FinishedGoodsReceiptRequestCommandResult(existing.Id, existing.RequestNo);
        }

        if (string.IsNullOrWhiteSpace(request.ProducedLotNo))
        {
            throw new KnownException("完工入库申请必须引用 MES 已生成的产出批次。");
        }

        var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
            cancellationToken)
            ?? throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        if (!string.Equals(workOrder.SkuId, request.SkuId, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException($"完工入库 SKU 与工单不一致，WorkOrderId = {request.WorkOrderId}");
        }

        if (!string.IsNullOrWhiteSpace(workOrder.UomCode) &&
            !string.Equals(workOrder.UomCode, request.UomCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnownException($"完工入库 UoM 与工单不一致，WorkOrderId = {request.WorkOrderId}");
        }

        // 引用的产出批次必须存在于 OutputLotGenealogies（报工时生成、冲销时删除）。用 AnyAsync 判存在（provider 中立，
        // 避免依赖空集 Sum 在 InMemory/Postgres 上的差异，见 ef-test-provider-translation-gap）。
        var outputLotExists = await dbContext.OutputLotGenealogies.AnyAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                x.ProducedLotNo == request.ProducedLotNo,
            cancellationToken);
        if (!outputLotExists)
        {
            throw new KnownException($"完工入库引用的产出批次不存在，ProducedLotNo = {request.ProducedLotNo}");
        }

        // MES receipt creation is a low-concurrency operator workflow; strict cross-command serialization would need a separate DB lock/constraint design.
        var activeReceiptQuantity = await ActiveReceiptRequestsForWorkOrder(
                dbContext.FinishedGoodsReceiptRequests,
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId)
            .SumAsync(x => x.Quantity, cancellationToken);
        if (activeReceiptQuantity + request.Quantity > workOrder.CompletedQuantity + FinishedGoodsReceiptRequest.QuantityTolerance)
        {
            throw new KnownException($"累计完工入库申请数量超过工单完工数量，WorkOrderId = {request.WorkOrderId}");
        }

        // 批次追溯完整性：单个产出批次的累计有效入库申请不得超过该批次产量（工单总量之外的更细粒度约束，
        // 防止把整张工单的完工量都登记到同一批次而破坏批次追溯）。批次存在性已由上方 AnyAsync 确认，故此 Sum 必 >0。
        var batchProducedQuantity = await dbContext.OutputLotGenealogies
            .Where(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                x.ProducedLotNo == request.ProducedLotNo)
            .SumAsync(x => x.Quantity, cancellationToken);
        var activeBatchReceiptQuantity = await ActiveReceiptRequestsForWorkOrder(
                dbContext.FinishedGoodsReceiptRequests,
                request.OrganizationId,
                request.EnvironmentId,
                request.WorkOrderId)
            .Where(x => x.ProducedLotNo == request.ProducedLotNo)
            .SumAsync(x => x.Quantity, cancellationToken);
        if (activeBatchReceiptQuantity + request.Quantity > batchProducedQuantity + FinishedGoodsReceiptRequest.QuantityTolerance)
        {
            throw new KnownException($"完工入库申请超过该产出批次可入库数量，ProducedLotNo = {request.ProducedLotNo}");
        }

        var receiptRequest = FinishedGoodsReceiptRequest.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.WorkOrderId,
            request.SkuId,
            request.Quantity,
            request.UomCode,
            request.RequestedAtUtc,
            request.ProducedLotNo,
            request.SerialNo,
            request.UnitCost,
            request.ProductionDate,
            request.ExpiryDate);
        dbContext.FinishedGoodsReceiptRequests.Add(receiptRequest);
        await Task.CompletedTask;
        return new FinishedGoodsReceiptRequestCommandResult(receiptRequest.Id, receiptRequest.RequestNo);
    }

    public static IQueryable<FinishedGoodsReceiptRequest> ActiveReceiptRequestsForWorkOrder(
        IQueryable<FinishedGoodsReceiptRequest> receiptRequests,
        string organizationId,
        string environmentId,
        string workOrderId)
    {
        return receiptRequests.Where(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.WorkOrderId == workOrderId &&
            x.Status != FinishedGoodsReceiptRequest.CancelledStatus);
    }
}

public sealed class RetryFinishedGoodsReceiptInventoryPostingCommandValidator
    : AbstractValidator<RetryFinishedGoodsReceiptInventoryPostingCommand>
{
    public RetryFinishedGoodsReceiptInventoryPostingCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RequestNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(200);
    }
}

public sealed class RetryFinishedGoodsReceiptInventoryPostingCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RetryFinishedGoodsReceiptInventoryPostingCommand, FinishedGoodsReceiptRequestCommandResult>
{
    public async Task<FinishedGoodsReceiptRequestCommandResult> Handle(
        RetryFinishedGoodsReceiptInventoryPostingCommand request,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.FinishedGoodsReceiptRequests.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.RequestNo == request.RequestNo,
            cancellationToken)
            ?? throw new KnownException($"未找到完工入库申请，RequestNo = {request.RequestNo}");

        try
        {
            receipt.RetryInventoryPosting(request.IdempotencyKey);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message);
        }

        await Task.CompletedTask;
        return new FinishedGoodsReceiptRequestCommandResult(receipt.Id, receipt.RequestNo);
    }
}
