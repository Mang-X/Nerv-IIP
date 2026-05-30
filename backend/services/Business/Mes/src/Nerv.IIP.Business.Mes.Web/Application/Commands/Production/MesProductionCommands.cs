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
    string? MaterialIssueRequestNo);

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
    IReadOnlyCollection<ConsumedMaterialLotInput>? ConsumedMaterialLots = null) : ICommand<ProductionReportCommandResult>;

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
        RuleFor(x => x).Must(x => x.GoodQuantity + x.ScrapQuantity > 0)
            .WithMessage("At least one reported quantity must be positive.");
        RuleForEach(x => x.ConsumedMaterialLots).ChildRules(lot =>
        {
            lot.RuleFor(x => x.MaterialId).NotEmpty().MaximumLength(100);
            lot.RuleFor(x => x.MaterialLotId).NotEmpty().MaximumLength(100);
            lot.RuleFor(x => x.ConsumedQuantity).GreaterThan(0);
            lot.RuleFor(x => x.MaterialIssueRequestNo).MaximumLength(100);
        });
    }
}

public sealed class RecordProductionReportCommandHandler(ApplicationDbContext dbContext, MesNumberingService? numberingService = null)
    : ICommandHandler<RecordProductionReportCommand, ProductionReportCommandResult>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<ProductionReportCommandResult> Handle(RecordProductionReportCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "production-report",
            "PRPT",
            null,
            request.IdempotencyKey,
            MesNumberingService.Fingerprint(request.WorkOrderId, request.OperationTaskId, request.GoodQuantity, request.ScrapQuantity, request.CompletesOperation, request.ReportedAtUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var existing = await dbContext.ProductionReports.SingleAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.ReportNo == allocation.Number,
                cancellationToken);
            return new ProductionReportCommandResult(existing.Id, existing.ReportNo);
        }

        var report = ProductionReport.Record(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.WorkOrderId,
            request.OperationTaskId,
            request.GoodQuantity,
            request.ScrapQuantity,
            request.CompletesOperation,
            request.ReportedAtUtc);
        dbContext.ProductionReports.Add(report);
        foreach (var lot in request.ConsumedMaterialLots ?? [])
        {
            if (!string.IsNullOrWhiteSpace(lot.MaterialIssueRequestNo))
            {
                var requestExists = await dbContext.MaterialIssueRequests.AnyAsync(
                    x => x.OrganizationId == request.OrganizationId &&
                        x.EnvironmentId == request.EnvironmentId &&
                        x.RequestNo == lot.MaterialIssueRequestNo &&
                        x.MaterialId == lot.MaterialId &&
                        x.MaterialLotId == lot.MaterialLotId &&
                        x.ReceivedQuantity >= lot.ConsumedQuantity,
                    cancellationToken);
                if (!requestExists)
                {
                    throw new KnownException($"报工引用的线边物料批次未接收或数量不足，MaterialLotId = {lot.MaterialLotId}");
                }
            }

            dbContext.ProductionReportMaterialConsumptions.Add(ProductionReportMaterialConsumption.Record(
                request.OrganizationId,
                request.EnvironmentId,
                report.ReportNo,
                request.WorkOrderId,
                request.OperationTaskId,
                lot.MaterialId,
                lot.MaterialLotId,
                lot.ConsumedQuantity,
                lot.MaterialIssueRequestNo));
        }

        await Task.CompletedTask;
        return new ProductionReportCommandResult(report.Id, report.ReportNo);
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
    string? IdempotencyKey = null) : ICommand<FinishedGoodsReceiptRequestCommandResult>;

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
    }
}

public sealed class CreateFinishedGoodsReceiptRequestCommandHandler(ApplicationDbContext dbContext, MesNumberingService? numberingService = null)
    : ICommandHandler<CreateFinishedGoodsReceiptRequestCommand, FinishedGoodsReceiptRequestCommandResult>
{
    private readonly MesNumberingService _numberingService = numberingService ?? new MesNumberingService();

    public async Task<FinishedGoodsReceiptRequestCommandResult> Handle(CreateFinishedGoodsReceiptRequestCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "finished-goods-receipt-request",
            "FGR",
            null,
            request.IdempotencyKey,
            MesNumberingService.Fingerprint(request.WorkOrderId, request.SkuId, request.Quantity, request.UomCode, request.RequestedAtUtc),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            var existing = await dbContext.FinishedGoodsReceiptRequests.SingleAsync(
                x => x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.RequestNo == allocation.Number,
                cancellationToken);
            return new FinishedGoodsReceiptRequestCommandResult(existing.Id, existing.RequestNo);
        }

        var receiptRequest = FinishedGoodsReceiptRequest.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.WorkOrderId,
            request.SkuId,
            request.Quantity,
            request.UomCode,
            request.RequestedAtUtc);
        dbContext.FinishedGoodsReceiptRequests.Add(receiptRequest);
        await Task.CompletedTask;
        return new FinishedGoodsReceiptRequestCommandResult(receiptRequest.Id, receiptRequest.RequestNo);
    }
}
