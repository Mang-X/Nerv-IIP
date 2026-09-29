using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

public sealed record GetBatchMaterialReadinessQuery(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> WorkOrderIds) : IQuery<MesMaterialReadinessBatchResponse>;

public sealed record MesMaterialReadinessBatchResponse(
    IReadOnlyList<MesMaterialReadinessResponse> Items);

public sealed class GetBatchMaterialReadinessQueryHandler(
    ApplicationDbContext dbContext,
    IMesMaterialReadinessLiveCoverageProvider liveCoverageProvider)
    : IQueryHandler<GetBatchMaterialReadinessQuery, MesMaterialReadinessBatchResponse>
{
    public async Task<MesMaterialReadinessBatchResponse> Handle(
        GetBatchMaterialReadinessQuery request,
        CancellationToken cancellationToken)
    {
        var ids = request.WorkOrderIds.Distinct(StringComparer.Ordinal).ToArray();
        var orders = await dbContext.WorkOrders.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                        x.EnvironmentId == request.EnvironmentId &&
                        ids.Contains(x.WorkOrderIdValue))
            .OrderBy(x => x.DueUtc)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.WorkOrderIdValue)
            .ToArrayAsync(cancellationToken);
        if (orders.Length != ids.Length)
        {
            var missing = ids.Except(orders.Select(x => x.WorkOrderIdValue), StringComparer.Ordinal).First();
            throw new KnownException($"未找到生产工单，WorkOrderId = {missing}");
        }

        var requirements = await MaterialRequirementSnapshotReader.LoadLatestByWorkOrdersAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, ids, cancellationToken);
        var requirementByOrder = requirements.GroupBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var issues = await dbContext.MaterialIssueRequests.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                        x.EnvironmentId == request.EnvironmentId &&
                        ids.Contains(x.WorkOrderId))
            .Select(x => new IssueRow(x.WorkOrderId, x.MaterialId, x.MaterialLotId,
                x.UomCode, x.RequestedQuantity, x.ReceivedQuantity, x.Status))
            .ToArrayAsync(cancellationToken);

        var lines = orders.SelectMany(order =>
        {
            if (!requirementByOrder.TryGetValue(order.WorkOrderIdValue, out var rows))
            {
                return [];
            }
            return rows.GroupBy(x => new MaterialKey(x.MaterialId, x.MaterialLotId, x.UomCode))
                .Select(group => new RequirementLine(
                    order.WorkOrderIdValue,
                    group.Key,
                    group.Sum(x => x.RequiredQuantity),
                    group.Sum(x => x.AvailableQuantity),
                    group.Sum(x => x.StagedQuantity),
                    group.Max(x => x.CapturedAtUtc),
                    MaterialSubstituteCandidateNormalizer.Normalize(
                        group.Key.MaterialId,
                        group.SelectMany(x => x.SubstituteMaterialIds))))
                .ToArray();
        }).ToArray();

        // Frozen availability is a shared material pool. Pick the newest capture of that pool;
        // the single-order snapshot remains authoritative for each order's demand and staging.
        var remaining = lines.GroupBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CapturedAtUtc)
                .ThenBy(y => y.WorkOrderId, StringComparer.Ordinal)
                .First().FrozenAvailable);
        var allocated = new Dictionary<(string WorkOrderId, MaterialKey Key), decimal>();
        foreach (var order in orders)
        {
            foreach (var line in lines.Where(x => x.WorkOrderId == order.WorkOrderIdValue))
            {
                var received = Received(issues, line);
                var quantity = Math.Min(remaining[line.Key],
                    Math.Max(0m, line.Required - line.Staged - received));
                allocated[(line.WorkOrderId, line.Key)] = quantity;
                remaining[line.Key] -= quantity;
            }
        }

        var coverageItems = lines.GroupBy(x => x.Key).Select(group =>
            new MesMaterialReadinessLiveCoverageRequestItem(
                group.Key.MaterialId, group.Key.MaterialLotId, group.Key.UomCode,
                group.Sum(x => x.Required),
                group.First().FrozenAvailable,
                group.Sum(x => x.Staged),
                group.Sum(x => Received(issues, x)),
                MaterialSubstituteCandidateNormalizer.Normalize(group.Key.MaterialId,
                    group.SelectMany(x => x.SubstituteMaterialIds))))
            .ToArray();
        var coverage = coverageItems.Length == 0
            ? new MesMaterialReadinessLiveCoverageResult(true, true, [])
            : await liveCoverageProvider.ResolveAsync(new MesMaterialReadinessLiveCoverageRequest(
                request.OrganizationId, request.EnvironmentId, coverageItems), cancellationToken);
        var etaByMaterial = coverage.Items.ToDictionary(x =>
            new MaterialKey(x.MaterialId, x.MaterialLotId, x.UomCode));

        var results = orders.Select(order =>
        {
            var orderLines = lines.Where(x => x.WorkOrderId == order.WorkOrderIdValue).ToArray();
            if (orderLines.Length == 0)
            {
                var proven = order.MaterialRequirementSnapshotStatus ==
                             WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus &&
                             order.MaterialRequirementSnapshotEvaluatedAtUtc is not null &&
                             order.MaterialRequirementSnapshotProductionVersionId == order.ProductionVersionId;
                return new MesMaterialReadinessResponse(order.WorkOrderIdValue,
                    proven ? "Ready" : "Blocked",
                    proven ? [] : [MaterialReadinessGuards.MissingRequirementSnapshotReason],
                    [], SnapshotCapturedAtUtc: proven ? order.MaterialRequirementSnapshotEvaluatedAtUtc : null);
            }

            var rows = orderLines.Select(line =>
            {
                var relatedIssues = issues.Where(x => x.WorkOrderId == line.WorkOrderId &&
                    string.Equals(x.MaterialId, line.Key.MaterialId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.UomCode, line.Key.UomCode, StringComparison.OrdinalIgnoreCase) &&
                    (line.Key.MaterialLotId is null ||
                     string.Equals(x.MaterialLotId, line.Key.MaterialLotId, StringComparison.OrdinalIgnoreCase))).ToArray();
                var requested = relatedIssues.Where(x => MaterialReadinessGuards.IsActiveIssueRequestStatus(x.Status))
                    .Sum(x => x.RequestedQuantity);
                var received = relatedIssues.Sum(x => x.ReceivedQuantity);
                var available = allocated[(line.WorkOrderId, line.Key)];
                var shortage = Math.Max(0m, line.Required - available - line.Staged - received);
                etaByMaterial.TryGetValue(line.Key, out var eta);
                return new MesMaterialReadinessRow(
                    line.Key.MaterialId, line.Key.MaterialLotId, line.Key.UomCode,
                    line.Required, available, requested, line.Staged, received, shortage,
                    shortage > 0 ? "Shortage" : "Ready",
                    shortage <= 0 ? MesMaterialShortageStages.None
                        : requested > received ? MesMaterialShortageStages.AwaitingDelivery
                        : MesMaterialShortageStages.AwaitingPreparation,
                    line.SubstituteMaterialIds,
                    shortage > 0 && coverage.ErpAvailable ? eta?.ExpectedAvailableAtUtc : null,
                    shortage > 0 && coverage.ErpAvailable ? eta?.ExpectedAvailabilitySource : null);
            }).OrderBy(x => x.MaterialId, StringComparer.OrdinalIgnoreCase)
              .ThenBy(x => x.MaterialLotId, StringComparer.OrdinalIgnoreCase)
              .ThenBy(x => x.UomCode, StringComparer.OrdinalIgnoreCase).ToArray();
            var reasons = rows.Where(x => x.ShortageQuantity > 0)
                .Select(x => MaterialReadinessGuards.FormatShortageReason(
                    x.MaterialId, x.MaterialLotId, x.ShortageQuantity)).ToArray();
            return new MesMaterialReadinessResponse(order.WorkOrderIdValue,
                reasons.Length > 0 ? "Blocked" : "Ready", reasons, rows,
                SnapshotCapturedAtUtc: orderLines.Max(x => x.CapturedAtUtc));
        }).ToArray();
        return new MesMaterialReadinessBatchResponse(results);
    }

    private static decimal Received(IEnumerable<IssueRow> issues, RequirementLine line) =>
        issues.Where(x => x.WorkOrderId == line.WorkOrderId &&
            string.Equals(x.MaterialId, line.Key.MaterialId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.UomCode, line.Key.UomCode, StringComparison.OrdinalIgnoreCase) &&
            (line.Key.MaterialLotId is null ||
             string.Equals(x.MaterialLotId, line.Key.MaterialLotId, StringComparison.OrdinalIgnoreCase)))
            .Sum(x => x.ReceivedQuantity);

    private sealed record MaterialKey(string MaterialId, string? MaterialLotId, string UomCode);
    private sealed record RequirementLine(string WorkOrderId, MaterialKey Key, decimal Required,
        decimal FrozenAvailable, decimal Staged, DateTimeOffset CapturedAtUtc,
        IReadOnlyCollection<string> SubstituteMaterialIds);
    private sealed record IssueRow(string WorkOrderId, string MaterialId, string? MaterialLotId,
        string UomCode, decimal RequestedQuantity, decimal ReceivedQuantity, string Status);
}
