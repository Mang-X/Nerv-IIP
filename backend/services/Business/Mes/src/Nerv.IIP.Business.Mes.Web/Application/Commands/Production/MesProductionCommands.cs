using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

public sealed record ProductionReportCommandResult(ProductionReportId Id, string ReportNo);

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
    string? SerialNo = null) : ICommand<ProductionReportCommandResult>;

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
            request.ProducedLotNo,
            request.SerialNo);

        var duplicateLot = (request.ConsumedMaterialLots ?? [])
            .GroupBy(x => $"{x.MaterialId.ToUpperInvariant()}|{x.MaterialLotId.ToUpperInvariant()}", StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateLot is not null)
        {
            var lot = duplicateLot.First();
            throw new KnownException($"报工耗料批次重复，MaterialId = {lot.MaterialId}, MaterialLotId = {lot.MaterialLotId}");
        }

        var materialConsumptions = new List<ProductionReportMaterialConsumption>();
        foreach (var lot in request.ConsumedMaterialLots ?? [])
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
        if (operationTask.OperationSequence == outputOperationSequence)
        {
            try
            {
                workOrder.RecordProductionProgress(request.GoodQuantity, request.ScrapQuantity, request.ReportedAtUtc);
            }
            catch (InvalidOperationException exception)
            {
                throw new KnownException(exception.Message);
            }
        }

        if (request.CompletesOperation)
        {
            try
            {
                operationTask.Complete(request.ReportedAtUtc);
            }
            catch (InvalidOperationException exception)
            {
                throw new KnownException(exception.Message);
            }
        }

        dbContext.ProductionReports.Add(report);
        dbContext.ProductionReportMaterialConsumptions.AddRange(materialConsumptions);
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
    string? SerialNo = null) : ICommand<FinishedGoodsReceiptRequestCommandResult>;

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
        RuleFor(x => x.UnitCost).NotNull().GreaterThan(0);
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
            MesCodingService.Fingerprint(request.WorkOrderId, request.SkuId, request.Quantity, request.UomCode, request.RequestedAtUtc, request.UnitCost, request.ProducedLotNo, request.SerialNo),
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
            request.UnitCost);
        dbContext.FinishedGoodsReceiptRequests.Add(receiptRequest);
        await Task.CompletedTask;
        return new FinishedGoodsReceiptRequestCommandResult(receiptRequest.Id, receiptRequest.RequestNo);
    }
}
