using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record ListWorkOrderCostsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId = null,
    string? SourceNcrId = null,
    string? SourceWorkOrderId = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListWorkOrderCostsResponse>;

public sealed class ListWorkOrderCostsQueryValidator : AbstractValidator<ListWorkOrderCostsQuery>
{
    public ListWorkOrderCostsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.WorkOrderId).MaximumLength(100);
        RuleFor(x => x.SourceNcrId).MaximumLength(100);
        RuleFor(x => x.SourceWorkOrderId).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed record ListWorkOrderCostsResponse(
    int Total,
    decimal OrdinaryCostTotal,
    decimal ReworkCostTotal,
    IReadOnlyCollection<WorkOrderCostListItem> Items);

public sealed record WorkOrderCostListItem(
    string WorkOrderCostId,
    string WorkOrderId,
    string SkuCode,
    string CostKind,
    string? SourceNcrId,
    string? SourceNcrCode,
    string? SourceWorkOrderId,
    decimal LaborCost,
    decimal MaterialCost,
    decimal MachineOverheadCost,
    decimal TotalAccumulatedCost,
    decimal CompletedQuantity,
    DateTimeOffset? CompletedAtUtc,
    int ExpectedReportCount,
    int ReceivedReportCount,
    int ExpectedMaterialMovementCount,
    int ReceivedMaterialMovementCount,
    bool CapitalizationPublished,
    decimal CapitalizedQuantity,
    decimal CapitalizedCost);

public sealed class ListWorkOrderCostsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWorkOrderCostsQuery, ListWorkOrderCostsResponse>
{
    public async Task<ListWorkOrderCostsResponse> Handle(
        ListWorkOrderCostsQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var costs = dbContext.WorkOrderCosts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkOrderId))
        {
            var workOrderId = request.WorkOrderId.Trim();
            costs = costs.Where(x => x.WorkOrderId == workOrderId);
        }
        if (!string.IsNullOrWhiteSpace(request.SourceNcrId))
        {
            var sourceNcrId = request.SourceNcrId.Trim();
            costs = costs.Where(x => x.SourceNcrId == sourceNcrId);
        }
        if (!string.IsNullOrWhiteSpace(request.SourceWorkOrderId))
        {
            var sourceWorkOrderId = request.SourceWorkOrderId.Trim();
            costs = costs.Where(x => x.SourceWorkOrderId == sourceWorkOrderId);
        }

        var total = await costs.CountAsync(cancellationToken);
        var ordinaryCostTotal = await SumCostAsync(
            costs.Where(x => x.SourceNcrId == null),
            cancellationToken);
        var reworkCostTotal = await SumCostAsync(
            costs.Where(x => x.SourceNcrId != null),
            cancellationToken);
        var items = await costs
            .OrderBy(x => x.WorkOrderId)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new WorkOrderCostListItem(
                x.Id.ToString(),
                x.WorkOrderId,
                x.SkuCode,
                x.SourceNcrId == null ? "ordinary" : QualityNcrDispositionTypes.Rework,
                x.SourceNcrId,
                x.SourceNcrCode,
                x.SourceWorkOrderId,
                x.Details.Where(detail => detail.Type == WorkOrderCostDetailType.Labor).Sum(detail => detail.Amount),
                x.Details.Where(detail => detail.Type == WorkOrderCostDetailType.Material).Sum(detail => detail.Amount),
                x.Details.Where(detail => detail.Type == WorkOrderCostDetailType.MachineOverhead).Sum(detail => detail.Amount),
                x.Details.Sum(detail => detail.Amount),
                x.CompletedQuantity,
                x.CompletedAtUtc,
                x.ExpectedReportCount,
                x.ReceivedReportCount,
                x.ExpectedMaterialMovementCount,
                x.ReceivedMaterialMovementCount,
                x.CapitalizationPublished,
                x.CapitalizedQuantity,
                x.CapitalizedCost))
            .ToArrayAsync(cancellationToken);

        return new(total, ordinaryCostTotal, reworkCostTotal, items);
    }

    private static async Task<decimal> SumCostAsync(
        IQueryable<WorkOrderCost> costs,
        CancellationToken cancellationToken)
    {
        return await costs
            .SelectMany(x => x.Details)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken)
            ?? 0m;
    }
}
