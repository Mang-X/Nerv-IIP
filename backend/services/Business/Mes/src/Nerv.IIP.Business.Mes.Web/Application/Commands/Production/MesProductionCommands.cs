using FluentValidation;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

public sealed record RecordProductionReportCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationTaskId,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    bool CompletesOperation,
    DateTimeOffset ReportedAtUtc) : ICommand<ProductionReportId>;

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
    }
}

public sealed class RecordProductionReportCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordProductionReportCommand, ProductionReportId>
{
    public async Task<ProductionReportId> Handle(RecordProductionReportCommand request, CancellationToken cancellationToken)
    {
        var report = ProductionReport.Record(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.OperationTaskId,
            request.GoodQuantity,
            request.ScrapQuantity,
            request.CompletesOperation,
            request.ReportedAtUtc);
        dbContext.ProductionReports.Add(report);
        await Task.CompletedTask;
        return report.Id;
    }
}

public sealed record CreateFinishedGoodsReceiptRequestCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string SkuId,
    decimal Quantity,
    string UomCode,
    DateTimeOffset RequestedAtUtc) : ICommand<FinishedGoodsReceiptRequestId>;

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

public sealed class CreateFinishedGoodsReceiptRequestCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateFinishedGoodsReceiptRequestCommand, FinishedGoodsReceiptRequestId>
{
    public async Task<FinishedGoodsReceiptRequestId> Handle(CreateFinishedGoodsReceiptRequestCommand request, CancellationToken cancellationToken)
    {
        var receiptRequest = FinishedGoodsReceiptRequest.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            request.SkuId,
            request.Quantity,
            request.UomCode,
            request.RequestedAtUtc);
        dbContext.FinishedGoodsReceiptRequests.Add(receiptRequest);
        await Task.CompletedTask;
        return receiptRequest.Id;
    }
}
