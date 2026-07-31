using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;

namespace Nerv.IIP.Business.Wms.Web.Application.Queries;

public sealed record GetWarehouseWorkScopeCatalogQuery(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes) : IQuery<WarehouseWorkScopeCatalog>;

public sealed class GetWarehouseWorkScopeCatalogQueryHandler(
    WarehouseWorkScopeAuthorizer authorizer)
    : IQueryHandler<GetWarehouseWorkScopeCatalogQuery, WarehouseWorkScopeCatalog>
{
    public Task<WarehouseWorkScopeCatalog> Handle(
        GetWarehouseWorkScopeCatalogQuery request,
        CancellationToken cancellationToken) =>
        authorizer.GetCatalogAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.ActorPrincipalId,
            request.AuthorizedSiteCodes,
            cancellationToken);
}

public sealed record ListWarehouseOperationalCandidatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string ScopeKind,
    string ScopeId,
    string CandidateDomain,
    IReadOnlyCollection<string>? AssignedOperatorUserIds = null,
    IReadOnlyCollection<string>? AssignedPoolCodes = null,
    IReadOnlyCollection<string>? SiteCodes = null,
    string? Keyword = null,
    string? SkuCode = null,
    string? LocationCode = null,
    int Take = 50,
    bool SiteWideScope = false) : IQuery<WarehouseOperationalCandidatesResponse>;

public static class WarehouseOperationalCandidateDomains
{
    public const string Receipts = "receipts";
    public const string Shipments = "shipments";
    public const string Counts = "counts";

    public static bool IsSupported(string? value) =>
        value is not null
        && (string.Equals(value.Trim(), Receipts, StringComparison.Ordinal)
            || string.Equals(value.Trim(), Shipments, StringComparison.Ordinal)
            || string.Equals(value.Trim(), Counts, StringComparison.Ordinal));
}

public sealed record WarehouseOperationalCandidatesResponse(
    string SourceKind,
    string ScopeKind,
    string ScopeId,
    DateTime AsOfUtc,
    DateTime? FreshnessUtc,
    bool Truncated,
    IReadOnlyCollection<WarehouseLocationCandidate> Locations,
    IReadOnlyCollection<WarehouseLotCandidate> Lots);

public sealed record WarehouseLocationCandidate(
    string SiteCode,
    string LocationCode,
    IReadOnlyCollection<string> SkuCodes,
    int ReferenceCount,
    DateTime LastObservedAtUtc);

public sealed record WarehouseLotCandidate(
    string SiteCode,
    string SkuCode,
    string LotNo,
    IReadOnlyCollection<string> LocationCodes,
    int ReferenceCount,
    DateTime LastObservedAtUtc);

public sealed class ListWarehouseOperationalCandidatesQueryHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IQueryHandler<
        ListWarehouseOperationalCandidatesQuery,
        WarehouseOperationalCandidatesResponse>
{
    public const string SourceKind = "wms-operational-facts";
    private const string ReplenishmentPendingLocation =
        "REPLENISHMENT-SOURCE-PENDING";

    public async Task<WarehouseOperationalCandidatesResponse> Handle(
        ListWarehouseOperationalCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var asOfUtc = timeProvider.GetUtcNow().UtcDateTime;
        var empty = Empty(request, asOfUtc);
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.EnvironmentId)
            || string.IsNullOrWhiteSpace(request.ScopeKind)
            || string.IsNullOrWhiteSpace(request.ScopeId)
            || !WarehouseOperationalCandidateDomains.IsSupported(
                request.CandidateDomain)
            || !WmsOwnershipQueryFilters.TryResolve(
                request.AssignedOperatorUserIds,
                request.AssignedPoolCodes,
                request.SiteCodes,
                WmsOwnershipQueryFilters.ModeOf(
                    organizationWideScope: false,
                    request.SiteWideScope),
                out var ownershipScope))
        {
            return empty;
        }

        var siteCodes = WmsOwnershipQueryFilters.Normalize(request.SiteCodes);
        if (siteCodes.Length == 0)
        {
            return empty;
        }

        var take = request.Take <= 0 ? 50 : Math.Clamp(request.Take, 1, 100);
        var sourceTake = Math.Clamp(take * 20, 100, 1_000);
        var candidateDomain = request.CandidateDomain.Trim();
        var inbound = ApplyScope(
                dbContext.InboundOrders
                    .AsNoTracking()
                    .Include(order => order.Lines)
                    .Where(order =>
                        order.OrganizationId == request.OrganizationId
                        && order.EnvironmentId == request.EnvironmentId
                        && siteCodes.Contains(order.SiteCode)),
                ownershipScope);
        var outbound = ApplyScope(
                dbContext.OutboundOrders
                    .AsNoTracking()
                    .Include(order => order.Lines)
                    .Where(order =>
                        order.OrganizationId == request.OrganizationId
                        && order.EnvironmentId == request.EnvironmentId
                        && siteCodes.Contains(order.SiteCode)),
                ownershipScope);
        var warehouseTasks = ApplyScope(
                dbContext.WarehouseTasks
                    .AsNoTracking()
                    .Where(task =>
                        task.OrganizationId == request.OrganizationId
                        && task.EnvironmentId == request.EnvironmentId
                        && siteCodes.Contains(task.SiteCode)),
                ownershipScope);
        var counts = ApplyScope(
                dbContext.CountExecutions
                    .AsNoTracking()
                    .Where(execution =>
                        execution.OrganizationId == request.OrganizationId
                        && execution.EnvironmentId == request.EnvironmentId
                        && siteCodes.Contains(execution.SiteCode)),
                ownershipScope);

        var inboundPage = EmptyPage<InboundOrder>();
        var outboundPage = EmptyPage<OutboundOrder>();
        var taskPage = EmptyPage<WarehouseTask>();
        var countPage = EmptyPage<CountExecution>();
        switch (candidateDomain)
        {
            case WarehouseOperationalCandidateDomains.Receipts:
                inboundPage = await ReadRecentAsync(
                    inbound.OrderByDescending(order => order.CreatedAtUtc),
                    sourceTake,
                    cancellationToken);
                taskPage = await ReadRecentAsync(
                    warehouseTasks
                        .Where(task => task.TaskType == WarehouseTaskType.Putaway)
                        .OrderByDescending(task => task.CreatedAtUtc),
                    sourceTake,
                    cancellationToken);
                break;
            case WarehouseOperationalCandidateDomains.Shipments:
                outboundPage = await ReadRecentAsync(
                    outbound.OrderByDescending(order => order.CreatedAtUtc),
                    sourceTake,
                    cancellationToken);
                taskPage = await ReadRecentAsync(
                    warehouseTasks
                        .Where(task => task.TaskType == WarehouseTaskType.Picking)
                        .OrderByDescending(task => task.CreatedAtUtc),
                    sourceTake,
                    cancellationToken);
                break;
            case WarehouseOperationalCandidateDomains.Counts:
                countPage = await ReadRecentAsync(
                    counts.OrderByDescending(execution => execution.CreatedAtUtc),
                    sourceTake,
                    cancellationToken);
                break;
        }
        var observations = inboundPage.Items
            .SelectMany(order => order.Lines.Select(line => new CandidateObservation(
                order.SiteCode,
                line.StagingLocationCode,
                line.SkuCode,
                line.LotNo,
                order.CreatedAtUtc)))
            .Concat(outboundPage.Items.SelectMany(order =>
                order.Lines.Select(line => new CandidateObservation(
                    order.SiteCode,
                    line.PickLocationCode,
                    line.SkuCode,
                    line.LotNo,
                    order.CreatedAtUtc))))
            .Concat(countPage.Items.Select(execution => new CandidateObservation(
                execution.SiteCode,
                execution.LocationCode,
                execution.SkuCode,
                LotNo: null,
                execution.CreatedAtUtc)))
            .Concat(taskPage.Items.SelectMany(task => new[]
            {
                new CandidateObservation(
                    task.SiteCode,
                    task.FromLocationCode,
                    task.SkuCode,
                    task.LotNo,
                    task.CreatedAtUtc),
                new CandidateObservation(
                    task.SiteCode,
                    task.ToLocationCode,
                    task.SkuCode,
                    task.LotNo,
                    task.CreatedAtUtc),
            }))
            .Where(observation =>
                !string.IsNullOrWhiteSpace(observation.LocationCode)
                && !string.Equals(
                    observation.LocationCode,
                    ReplenishmentPendingLocation,
                    StringComparison.Ordinal))
            .ToArray();

        var skuCode = Optional(request.SkuCode);
        var locationCode = Optional(request.LocationCode);
        var keyword = Optional(request.Keyword)?.ToUpperInvariant();
        var filtered = observations
            .Where(observation =>
                skuCode is null
                || string.Equals(
                    observation.SkuCode,
                    skuCode,
                    StringComparison.Ordinal))
            .Where(observation =>
                locationCode is null
                || string.Equals(
                    observation.LocationCode,
                    locationCode,
                    StringComparison.Ordinal))
            .Where(observation =>
                keyword is null
                || observation.LocationCode.ToUpperInvariant().Contains(
                    keyword,
                    StringComparison.Ordinal)
                || observation.SkuCode.ToUpperInvariant().Contains(
                    keyword,
                    StringComparison.Ordinal)
                || observation.LotNo?.ToUpperInvariant().Contains(
                    keyword,
                    StringComparison.Ordinal) == true)
            .ToArray();

        var locationCandidates = filtered
            .GroupBy(
                observation => (observation.SiteCode, observation.LocationCode))
            .Select(group => new WarehouseLocationCandidate(
                group.Key.SiteCode,
                group.Key.LocationCode,
                group.Select(observation => observation.SkuCode)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                group.Count(),
                group.Max(observation => observation.ObservedAtUtc)))
            .OrderByDescending(candidate => candidate.LastObservedAtUtc)
            .ThenBy(candidate => candidate.LocationCode, StringComparer.Ordinal)
            .ToArray();
        var lotCandidates = filtered
            .Where(observation => !string.IsNullOrWhiteSpace(observation.LotNo))
            .GroupBy(observation => (
                observation.SiteCode,
                observation.SkuCode,
                LotNo: observation.LotNo!))
            .Select(group => new WarehouseLotCandidate(
                group.Key.SiteCode,
                group.Key.SkuCode,
                group.Key.LotNo,
                group.Select(observation => observation.LocationCode)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                group.Count(),
                group.Max(observation => observation.ObservedAtUtc)))
            .OrderByDescending(candidate => candidate.LastObservedAtUtc)
            .ThenBy(candidate => candidate.LotNo, StringComparer.Ordinal)
            .ToArray();
        return new WarehouseOperationalCandidatesResponse(
            SourceKind,
            request.ScopeKind.Trim().ToLowerInvariant(),
            request.ScopeId.Trim(),
            asOfUtc,
            observations.Length == 0
                ? null
                : observations.Max(observation => observation.ObservedAtUtc),
            inboundPage.Truncated
                || outboundPage.Truncated
                || taskPage.Truncated
                || countPage.Truncated
                || locationCandidates.Length > take
                || lotCandidates.Length > take,
            locationCandidates.Take(take).ToArray(),
            lotCandidates.Take(take).ToArray());
    }

    private static WarehouseOperationalCandidatesResponse Empty(
        ListWarehouseOperationalCandidatesQuery request,
        DateTime asOfUtc) =>
        new(
            SourceKind,
            request.ScopeKind?.Trim().ToLowerInvariant() ?? string.Empty,
            request.ScopeId?.Trim() ?? string.Empty,
            asOfUtc,
            FreshnessUtc: null,
            Truncated: false,
            Locations: [],
            Lots: []);

    private static IQueryable<InboundOrder> ApplyScope(
        IQueryable<InboundOrder> query,
        WmsOwnershipScope scope) =>
        scope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(order =>
                order.AssignedOperatorUserId != null
                && scope.Values.Contains(order.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(order =>
                order.AssignedPoolCode != null
                && scope.Values.Contains(order.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };

    private static IQueryable<OutboundOrder> ApplyScope(
        IQueryable<OutboundOrder> query,
        WmsOwnershipScope scope) =>
        scope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(order =>
                order.AssignedOperatorUserId != null
                && scope.Values.Contains(order.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(order =>
                order.AssignedPoolCode != null
                && scope.Values.Contains(order.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };

    private static IQueryable<WarehouseTask> ApplyScope(
        IQueryable<WarehouseTask> query,
        WmsOwnershipScope scope) =>
        scope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(task =>
                task.AssignedOperatorUserId != null
                && scope.Values.Contains(task.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(task =>
                task.AssignedPoolCode != null
                && scope.Values.Contains(task.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };

    private static IQueryable<CountExecution> ApplyScope(
        IQueryable<CountExecution> query,
        WmsOwnershipScope scope) =>
        scope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(execution =>
                execution.AssignedOperatorUserId != null
                && scope.Values.Contains(execution.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(execution =>
                execution.AssignedPoolCode != null
                && scope.Values.Contains(execution.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };

    private static async Task<CandidatePage<T>> ReadRecentAsync<T>(
        IQueryable<T> query,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        return new CandidatePage<T>(
            rows.Take(take).ToArray(),
            rows.Length > take);
    }

    private static CandidatePage<T> EmptyPage<T>() =>
        new([], Truncated: false);

    private static string? Optional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed record CandidateObservation(
        string SiteCode,
        string LocationCode,
        string SkuCode,
        string? LotNo,
        DateTime ObservedAtUtc);

    private sealed record CandidatePage<T>(
        IReadOnlyCollection<T> Items,
        bool Truncated);
}

public sealed record ListBackorderOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null) : IQuery<ListBackorderOrdersResponse>;

public sealed record ListBackorderOrdersResponse(IReadOnlyCollection<BackorderOrderFact> Items, int Total);

public sealed record BackorderOrderFact(
    BackorderOrderId BackorderOrderId,
    string BackorderOrderNo,
    string OutboundOrderNo,
    string OutboundOrderLineNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string PickLocationCode,
    decimal BackorderQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc,
    string? ClosureReason);

public sealed class ListBackorderOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListBackorderOrdersQuery, ListBackorderOrdersResponse>
{
    public async Task<ListBackorderOrdersResponse> Handle(ListBackorderOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.BackorderOrders.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (WmsListQueryFilters.TryParseStatus<BackorderOrderStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListBackorderOrdersResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.BackorderOrderNo.ToUpper().Contains(keyword)
                || x.OutboundOrderNo.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.BackorderOrderNo)
            .Skip(skip).Take(take)
            .Select(x => new BackorderOrderFact(x.Id, x.BackorderOrderNo, x.OutboundOrderNo, x.OutboundOrderLineNo,
                x.SkuCode, x.UomCode, x.SiteCode, x.PickLocationCode, x.BackorderQuantity, x.Status.ToString(),
                x.CreatedAtUtc, x.ClosedAtUtc, x.ClosureReason))
            .ToArrayAsync(cancellationToken);
        return new ListBackorderOrdersResponse(items, total);
    }
}

public sealed record ListInboundOrdersQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null,
    InboundOrderId? InboundOrderId = null,
    string? LocationCode = null,
    string? LotNo = null,
    IReadOnlyCollection<string>? AssignedOperatorUserIds = null,
    IReadOnlyCollection<string>? AssignedPoolCodes = null,
    IReadOnlyCollection<string>? SiteCodes = null,
    bool OrganizationWideScope = false,
    bool SiteWideScope = false) : IQuery<ListInboundOrdersResponse>;

public sealed record ListInboundOrdersResponse(IReadOnlyCollection<InboundOrderListItem> Items, int Total);

public sealed record InboundOrderListItem(
    InboundOrderId InboundOrderId,
    string InboundOrderNo,
    string Status,
    DateTime CreatedAtUtc,
    // 单据级派生质检状态（聚合全部收货行含免检；无行为空串）与上架放行判据，
    // 供 PDA/console 列表状态标与上架门禁一次查询即得，避免按分页门禁行跨页聚合错误。
    string QualityGateStatus,
    bool IsReleasedForPutaway,
    string SiteCode,
    string? AssignedOperatorUserId,
    string? AssignedPoolCode,
    long Version);

internal static class InboundOrderQualityAggregate
{
    // 优先级：不合格 > 待检 > 有条件放行 > 合格 > 免检；无行返回空串（未收货，无状态标）。
    public static string Derive(bool hasAnyLine, bool hasRejected, bool hasPending, bool hasConditional, bool hasPassed)
    {
        if (!hasAnyLine) return string.Empty;
        if (hasRejected) return InboundQualityGateStatuses.Rejected;
        if (hasPending) return InboundQualityGateStatuses.Pending;
        if (hasConditional) return InboundQualityGateStatuses.ConditionalReleased;
        if (hasPassed) return InboundQualityGateStatuses.Passed;
        return InboundQualityGateStatuses.NotRequired;
    }

    // 整单可上架：至少一行且无任何一行待检/不合格（其余为合格/有条件放行/免检）。
    public static bool ReleasedForPutaway(bool hasAnyLine, bool hasRejected, bool hasPending)
        => hasAnyLine && !hasRejected && !hasPending;
}

public sealed class ListInboundOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInboundOrdersQuery, ListInboundOrdersResponse>
{
    public async Task<ListInboundOrdersResponse> Handle(ListInboundOrdersQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return new ListInboundOrdersResponse([], 0);
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.InboundOrders
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.InboundOrderId == null || x.Id == request.InboundOrderId);
        if (!WmsOwnershipQueryFilters.TryResolve(
                request.AssignedOperatorUserIds,
                request.AssignedPoolCodes,
                request.SiteCodes,
                WmsOwnershipQueryFilters.ModeOf(
                    request.OrganizationWideScope,
                    request.SiteWideScope),
                out var ownershipScope))
        {
            return new ListInboundOrdersResponse([], 0);
        }
        query = ownershipScope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(x =>
                x.AssignedOperatorUserId != null
                && ownershipScope.Values.Contains(x.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(x =>
                x.AssignedPoolCode != null
                && ownershipScope.Values.Contains(x.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };
        var siteCodes = WmsOwnershipQueryFilters.Normalize(request.SiteCodes);
        if (siteCodes.Length > 0)
        {
            query = query.Where(x => siteCodes.Contains(x.SiteCode));
        }
        if (WmsListQueryFilters.TryParseStatus<InboundOrderStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListInboundOrdersResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.InboundOrderNo.ToUpper().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.Lines.Any(line => line.StagingLocationCode == request.LocationCode));
        }

        if (!string.IsNullOrWhiteSpace(request.LotNo))
        {
            query = query.Where(x => x.Lines.Any(line => line.LotNo == request.LotNo));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.InboundOrderNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.InboundOrderNo,
                Status = x.Status.ToString(),
                x.CreatedAtUtc,
                x.SiteCode,
                x.AssignedOperatorUserId,
                x.AssignedPoolCode,
                x.Version,
                HasAnyLine = x.Lines.Any(),
                HasRejected = x.Lines.Any(l => l.QualityGateStatus == InboundQualityGateStatuses.Rejected),
                HasPending = x.Lines.Any(l => l.QualityGateStatus == InboundQualityGateStatuses.Pending),
                HasConditional = x.Lines.Any(l => l.QualityGateStatus == InboundQualityGateStatuses.ConditionalReleased),
                HasPassed = x.Lines.Any(l => l.QualityGateStatus == InboundQualityGateStatuses.Passed),
            })
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(x => new InboundOrderListItem(
                x.Id,
                x.InboundOrderNo,
                x.Status,
                x.CreatedAtUtc,
                InboundOrderQualityAggregate.Derive(x.HasAnyLine, x.HasRejected, x.HasPending, x.HasConditional, x.HasPassed),
                InboundOrderQualityAggregate.ReleasedForPutaway(x.HasAnyLine, x.HasRejected, x.HasPending),
                x.SiteCode,
                x.AssignedOperatorUserId,
                x.AssignedPoolCode,
                x.Version))
            .ToArray();
        return new ListInboundOrdersResponse(items, total);
    }
}

public sealed record ListOutboundOrdersQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null,
    OutboundOrderId? OutboundOrderId = null,
    string? LocationCode = null,
    string? LotNo = null,
    IReadOnlyCollection<string>? AssignedOperatorUserIds = null,
    IReadOnlyCollection<string>? AssignedPoolCodes = null,
    IReadOnlyCollection<string>? SiteCodes = null,
    bool OrganizationWideScope = false,
    bool SiteWideScope = false) : IQuery<ListOutboundOrdersResponse>;

public sealed record ListOutboundOrdersResponse(IReadOnlyCollection<OutboundOrderListItem> Items, int Total);

public sealed record OutboundOrderListItem(
    OutboundOrderId OutboundOrderId,
    string OutboundOrderNo,
    string Status,
    string SiteCode,
    string InventoryPostingStatus,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyCollection<OutboundOrderLineListItem> Lines,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? AssignedOperatorUserId,
    string? AssignedPoolCode,
    long Version);

public sealed record OutboundOrderLineListItem(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal RequestedQuantity,
    decimal IssuedQuantity,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    string InventoryPostingStatus,
    string? FailureCode,
    string? FailureMessage);

public sealed class ListOutboundOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOutboundOrdersQuery, ListOutboundOrdersResponse>
{
    public async Task<ListOutboundOrdersResponse> Handle(ListOutboundOrdersQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return new ListOutboundOrdersResponse([], 0);
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.OutboundOrders
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.OutboundOrderId == null || x.Id == request.OutboundOrderId);
        if (!WmsOwnershipQueryFilters.TryResolve(
                request.AssignedOperatorUserIds,
                request.AssignedPoolCodes,
                request.SiteCodes,
                WmsOwnershipQueryFilters.ModeOf(
                    request.OrganizationWideScope,
                    request.SiteWideScope),
                out var ownershipScope))
        {
            return new ListOutboundOrdersResponse([], 0);
        }
        query = ownershipScope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(x =>
                x.AssignedOperatorUserId != null
                && ownershipScope.Values.Contains(x.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(x =>
                x.AssignedPoolCode != null
                && ownershipScope.Values.Contains(x.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };
        var siteCodes = WmsOwnershipQueryFilters.Normalize(request.SiteCodes);
        if (siteCodes.Length > 0)
        {
            query = query.Where(x => siteCodes.Contains(x.SiteCode));
        }
        if (WmsListQueryFilters.TryParseStatus<OutboundOrderStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListOutboundOrdersResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.OutboundOrderNo.ToUpper().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.Lines.Any(line => line.PickLocationCode == request.LocationCode));
        }

        if (!string.IsNullOrWhiteSpace(request.LotNo))
        {
            query = query.Where(x => x.Lines.Any(line => line.LotNo == request.LotNo));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.OutboundOrderNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new
            {
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.OutboundOrderNo,
                Status = x.Status.ToString(),
                x.SiteCode,
                x.CreatedAtUtc,
                x.CompletedAtUtc,
                x.AssignedOperatorUserId,
                x.AssignedPoolCode,
                x.Version,
                Lines = x.Lines
                    .OrderBy(line => line.LineNo)
                    .Select(line => new
                    {
                        line.LineNo,
                        line.SkuCode,
                        line.UomCode,
                        line.RequestedQuantity,
                        line.IssuedQuantity,
                        LocationCode = line.PickLocationCode,
                        line.LotNo,
                        line.SerialNo,
                        line.QualityStatus,
                        line.OwnerType,
                        line.OwnerId,
                    })
                    .ToArray(),
            })
            .ToArrayAsync(cancellationToken);
        var orderNos = rows.Select(x => x.OutboundOrderNo).Distinct(StringComparer.Ordinal).ToArray();
        var postingRequests = orderNos.Length == 0
            ? []
            : await dbContext.InventoryMovementRequests
                .AsNoTracking()
                .Where(x => x.MovementType == "outbound" && orderNos.Contains(x.SourceDocumentId))
                .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
                .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId)
                .ToArrayAsync(cancellationToken);
        var latestRequests = postingRequests
            .GroupBy(x => (x.OrganizationId, x.EnvironmentId, x.SourceDocumentId))
            .SelectMany(document => InventoryMovementRequestAttempts.LatestByLine(document)
                .Select(line => new
                {
                    Key = (document.Key.OrganizationId, document.Key.EnvironmentId, document.Key.SourceDocumentId, LineNo: line.Key),
                    Request = line.Value,
                }))
            .ToDictionary(
                x => x.Key,
                x => x.Request);
        var items = rows.Select(row =>
        {
            var lineItems = row.Lines.Select(line =>
            {
                latestRequests.TryGetValue(
                    (row.OrganizationId, row.EnvironmentId, row.OutboundOrderNo, line.LineNo),
                    out var latestRequest);
                return new OutboundOrderLineListItem(
                    line.LineNo,
                    line.SkuCode,
                    line.UomCode,
                    line.RequestedQuantity,
                    line.IssuedQuantity,
                    line.LocationCode,
                    line.LotNo,
                    line.SerialNo,
                    line.QualityStatus,
                    line.OwnerType,
                    line.OwnerId,
                    PostingStatus(latestRequest),
                    latestRequest?.FailureCode,
                    latestRequest?.FailureMessage);
            }).ToArray();
            var failedLine = lineItems.FirstOrDefault(line => line.InventoryPostingStatus == "failed");
            return new OutboundOrderListItem(
                row.Id,
                row.OutboundOrderNo,
                row.Status,
                row.SiteCode,
                AggregatePostingStatus(lineItems, row.Status),
                failedLine?.FailureCode,
                failedLine?.FailureMessage,
                lineItems,
                row.CreatedAtUtc,
                row.CompletedAtUtc,
                row.AssignedOperatorUserId,
                row.AssignedPoolCode,
                row.Version);
        }).ToArray();
        return new ListOutboundOrdersResponse(items, total);
    }

    private static string PostingStatus(InventoryMovementRequest? request) =>
        request?.Status switch
        {
            InventoryMovementRequestStatus.Pending => "pending",
            InventoryMovementRequestStatus.Posted => "posted",
            InventoryMovementRequestStatus.Failed => "failed",
            _ => "not-started",
        };

    private static string AggregatePostingStatus(
        IReadOnlyCollection<OutboundOrderLineListItem> lines,
        string orderStatus)
    {
        if (lines.Any(line => line.InventoryPostingStatus == "failed"))
        {
            return "failed";
        }

        if (lines.Any(line => line.InventoryPostingStatus == "pending"))
        {
            return "pending";
        }

        if (lines.Count > 0 && lines.All(line => line.InventoryPostingStatus == "posted"))
        {
            return "posted";
        }

        return string.Equals(orderStatus, OutboundOrderStatus.Completed.ToString(), StringComparison.Ordinal)
            ? "posted"
            : "not-started";
    }
}

public sealed record ListWarehouseTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    WarehouseTaskType TaskType,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? Keyword = null,
    string? LotNo = null,
    IReadOnlyCollection<string>? AssignedOperatorUserIds = null,
    IReadOnlyCollection<string>? AssignedPoolCodes = null,
    IReadOnlyCollection<string>? SiteCodes = null,
    bool OrganizationWideScope = false,
    string? ActorPrincipalId = null,
    bool SiteWideScope = false) : IQuery<ListWarehouseTasksResponse>;

public sealed record ListWarehouseTasksResponse(IReadOnlyCollection<WarehouseTaskFact> Items, int Total);

public sealed record WarehouseTaskFact(
    WarehouseTaskId WarehouseTaskId,
    string OrganizationId,
    string EnvironmentId,
    string TaskType,
    string TaskNo,
    string SourceOrderNo,
    string SourceOrderLineNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string FromLocationCode,
    string ToLocationCode,
    string? AssignedOperatorUserId,
    string? AssignedPoolCode,
    string? LotNo,
    string? SerialNo,
    decimal PlannedQuantity,
    decimal ExecutedQuantity,
    string Status,
    long Version,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyCollection<string> AllowedActions,
    IReadOnlyCollection<string> BlockReasons);

public sealed class ListWarehouseTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWarehouseTasksQuery, ListWarehouseTasksResponse>
{
    public async Task<ListWarehouseTasksResponse> Handle(ListWarehouseTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.WarehouseTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.TaskType == request.TaskType);
        if (!WmsOwnershipQueryFilters.TryResolve(
                request.AssignedOperatorUserIds,
                request.AssignedPoolCodes,
                request.SiteCodes,
                WmsOwnershipQueryFilters.ModeOf(
                    request.OrganizationWideScope,
                    request.SiteWideScope),
                out var ownershipScope))
        {
            return new ListWarehouseTasksResponse([], 0);
        }
        query = ownershipScope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(x =>
                x.AssignedOperatorUserId != null
                && ownershipScope.Values.Contains(x.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(x =>
                x.AssignedPoolCode != null
                && ownershipScope.Values.Contains(x.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };
        var siteCodes = WmsOwnershipQueryFilters.Normalize(request.SiteCodes);
        if (siteCodes.Length > 0)
        {
            query = query.Where(x => siteCodes.Contains(x.SiteCode));
        }

        if (WmsListQueryFilters.TryParseStatus<WarehouseTaskStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListWarehouseTasksResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.FromLocationCode == request.LocationCode || x.ToLocationCode == request.LocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.LotNo))
        {
            query = query.Where(x => x.LotNo == request.LotNo);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.TaskNo.ToUpper().Contains(keyword)
                || x.SourceOrderNo.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.TaskNo)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(x =>
            {
                var presentation = WarehouseTaskQueryPresentation.Evaluate(
                    x,
                    request.ActorPrincipalId);
                return new WarehouseTaskFact(
                    x.Id,
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.TaskType.ToString(),
                    x.TaskNo,
                    x.SourceOrderNo,
                    x.SourceOrderLineNo,
                    x.SkuCode,
                    x.UomCode,
                    x.SiteCode,
                    x.FromLocationCode,
                    x.ToLocationCode,
                    x.AssignedOperatorUserId,
                    x.AssignedPoolCode,
                    x.LotNo,
                    x.SerialNo,
                    x.PlannedQuantity,
                    x.ExecutedQuantity,
                    x.Status.ToString(),
                    x.Version,
                    x.CreatedAtUtc,
                    x.CompletedAtUtc,
                    presentation.AllowedActions,
                    presentation.BlockReasons);
            })
            .ToArray();
        return new ListWarehouseTasksResponse(items, total);
    }
}

internal sealed record WarehouseTaskActionPresentation(
    IReadOnlyCollection<string> AllowedActions,
    IReadOnlyCollection<string> BlockReasons);

internal static class WarehouseTaskQueryPresentation
{
    public static WarehouseTaskActionPresentation Evaluate(
        WarehouseTask task,
        string? actorPrincipalId)
    {
        if (task.Status is not (
            WarehouseTaskStatus.Open
            or WarehouseTaskStatus.InProgress))
        {
            return Blocked("TASK_TERMINAL");
        }

        if (task.TaskType is not (
            WarehouseTaskType.Putaway
            or WarehouseTaskType.Picking))
        {
            return Blocked("TASK_TYPE_NOT_MANUALLY_EXECUTABLE");
        }

        var actor = WmsText.Optional(actorPrincipalId);
        if (actor is null)
        {
            return Blocked("ACTOR_CONTEXT_MISSING");
        }

        var blockReasons = new List<string>();
        if (string.IsNullOrWhiteSpace(task.AssignedPoolCode))
        {
            blockReasons.Add("TASK_NOT_ASSIGNED_TO_WORK_POOL");
        }

        if (!string.IsNullOrWhiteSpace(task.AssignedOperatorUserId)
            && !string.Equals(
                task.AssignedOperatorUserId,
                actor,
                StringComparison.Ordinal))
        {
            blockReasons.Add("TASK_ASSIGNED_TO_ANOTHER_OPERATOR");
        }

        switch (task.ExecutionChannel)
        {
            case WarehouseTaskExecutionChannel.Wcs:
                blockReasons.Add("TASK_EXECUTION_CLAIMED_BY_WCS");
                break;
            case WarehouseTaskExecutionChannel.Manual
                when !string.Equals(
                    task.ExecutionClaimedBy,
                    actor,
                    StringComparison.Ordinal):
                blockReasons.Add("TASK_EXECUTION_CLAIMED_BY_ANOTHER_OPERATOR");
                break;
            case WarehouseTaskExecutionChannel.LegacyUnclaimed
                or WarehouseTaskExecutionChannel.Unclaimed
                when task.Status == WarehouseTaskStatus.InProgress:
                blockReasons.Add("TASK_EXECUTION_NOT_CLAIMED");
                break;
        }

        if (blockReasons.Count > 0)
        {
            return new WarehouseTaskActionPresentation([], blockReasons);
        }

        return new WarehouseTaskActionPresentation(
            task.Status switch
            {
                WarehouseTaskStatus.Open => ["start"],
                WarehouseTaskStatus.InProgress => ["progress", "exception", "complete"],
                _ => [],
            },
            []);
    }

    public static WarehouseTaskActionPresentation StateChangedSinceReceipt() =>
        Blocked("TASK_STATE_CHANGED_SINCE_RECEIPT");

    private static WarehouseTaskActionPresentation Blocked(string reason) =>
        new([], [reason]);
}

internal static class WmsOwnershipQueryFilters
{
    public static string[] Normalize(IEnumerable<string>? values) =>
        values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray()
        ?? [];

    /// <summary>
    /// 归属模式是三选一，用一个枚举表达，不用两个布尔互相约束——布尔组合能拼出
    /// 「既组织全量又站点整站」这种无意义状态，读代码的人要靠分支顺序才能反推真实语义。
    /// </summary>
    public static WmsOwnershipScopeMode ModeOf(
        bool organizationWideScope,
        bool siteWideScope) =>
        organizationWideScope
            ? WmsOwnershipScopeMode.OrganizationWide
            : siteWideScope
                ? WmsOwnershipScopeMode.SiteWide
                : WmsOwnershipScopeMode.Assignment;

    public static bool TryResolve(
        IEnumerable<string>? operatorUserIds,
        IEnumerable<string>? poolCodes,
        IEnumerable<string>? siteCodes,
        WmsOwnershipScopeMode mode,
        out WmsOwnershipScope scope)
    {
        var operators = Normalize(operatorUserIds);
        var pools = Normalize(poolCodes);
        var sites = Normalize(siteCodes);
        var assignmentModes = (operators.Length > 0 ? 1 : 0)
            + (pools.Length > 0 ? 1 : 0);
        switch (mode)
        {
            // 组织全量读面不成立，永远 fail closed。
            case WmsOwnershipScopeMode.OrganizationWide:
                scope = default;
                return false;

            // 站点范围：站内整站作业面，不再按作业池/操作人收窄，但必须有明确站点边界。
            case WmsOwnershipScopeMode.SiteWide when sites.Length > 0 && assignmentModes == 0:
                scope = new WmsOwnershipScope(WmsOwnershipScopeKind.Site, sites);
                return true;

            case WmsOwnershipScopeMode.SiteWide:
                scope = default;
                return false;

            // 归属范围：操作人与作业池二选一，既不能同时给也不能都不给。
            case WmsOwnershipScopeMode.Assignment when assignmentModes == 1:
                scope = operators.Length > 0
                    ? new WmsOwnershipScope(WmsOwnershipScopeKind.Operator, operators)
                    : new WmsOwnershipScope(WmsOwnershipScopeKind.Pool, pools);
                return true;

            default:
                scope = default;
                return false;
        }
    }
}

internal enum WmsOwnershipScopeMode
{
    /// <summary>按 self / work-pool 归属收窄。</summary>
    Assignment,

    /// <summary>按 IAM 精确站点授权覆盖整站。</summary>
    SiteWide,

    /// <summary>组织全量——不成立，只用于显式拒绝。</summary>
    OrganizationWide,
}

internal enum WmsOwnershipScopeKind
{
    Operator,
    Pool,
    Site,
}

internal readonly record struct WmsOwnershipScope(
    WmsOwnershipScopeKind Kind,
    string[] Values);

public sealed record ListCountExecutionsQuery(
    string OrganizationId,
    string EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? LocationCode = null,
    string? Keyword = null,
    CountExecutionId? CountExecutionId = null,
    IReadOnlyCollection<string>? AssignedOperatorUserIds = null,
    IReadOnlyCollection<string>? AssignedPoolCodes = null,
    IReadOnlyCollection<string>? SiteCodes = null,
    bool OrganizationWideScope = false,
    bool SiteWideScope = false) : IQuery<ListCountExecutionsResponse>;

public sealed record ListCountExecutionsResponse(IReadOnlyCollection<CountExecutionFact> Items, int Total);

public sealed record CountExecutionFact(
    CountExecutionId CountExecutionId,
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    decimal ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? VarianceQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? InventoryPostingStatus = null,
    string? InventoryPostingFailureCode = null,
    string? InventoryPostingFailureMessage = null,
    string? InventoryMovementId = null,
    string? AssignedOperatorUserId = null,
    string? AssignedPoolCode = null,
    long Version = 0);

public sealed class ListCountExecutionsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListCountExecutionsQuery, ListCountExecutionsResponse>
{
    public async Task<ListCountExecutionsResponse> Handle(ListCountExecutionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.CountExecutions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.CountExecutionId == null || x.Id == request.CountExecutionId);
        if (!WmsOwnershipQueryFilters.TryResolve(
                request.AssignedOperatorUserIds,
                request.AssignedPoolCodes,
                request.SiteCodes,
                WmsOwnershipQueryFilters.ModeOf(
                    request.OrganizationWideScope,
                    request.SiteWideScope),
                out var ownershipScope))
        {
            return new ListCountExecutionsResponse([], 0);
        }
        query = ownershipScope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => query.Where(x =>
                x.AssignedOperatorUserId != null
                && ownershipScope.Values.Contains(x.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => query.Where(x =>
                x.AssignedPoolCode != null
                && ownershipScope.Values.Contains(x.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => query,
            _ => query.Where(_ => false),
        };
        var siteCodes = WmsOwnershipQueryFilters.Normalize(request.SiteCodes);
        if (siteCodes.Length > 0)
        {
            query = query.Where(x => siteCodes.Contains(x.SiteCode));
        }

        if (WmsListQueryFilters.TryParseStatus<CountExecutionStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListCountExecutionsResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.LocationCode == request.LocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.CountNo.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword)
                || x.LocationCode.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.CountNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new CountExecutionFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.CountNo,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.ExpectedQuantity,
                x.CountedQuantity,
                x.VarianceQuantity,
                x.Status.ToString(),
                x.CreatedAtUtc,
                x.CompletedAtUtc,
                null,
                null,
                null,
                null,
                x.AssignedOperatorUserId,
                x.AssignedPoolCode,
                x.Version))
            .ToArrayAsync(cancellationToken);
        var countNumbers = items.Select(x => x.CountNo).ToArray();
        var movementRequests = await dbContext.InventoryMovementRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.MovementType == "count-adjustment"
                && countNumbers.Contains(x.SourceDocumentId)
                && x.SourceDocumentLineId == null)
            .OrderBy(x => x.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        var movementByCountNumber = movementRequests
            .GroupBy(x => x.SourceDocumentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var projectedItems = items
            .Select(item => movementByCountNumber.TryGetValue(item.CountNo, out var movement)
                ? item with
                {
                    InventoryPostingStatus = movement.Status.ToString(),
                    InventoryPostingFailureCode = movement.FailureCode,
                    InventoryPostingFailureMessage = movement.FailureMessage,
                    InventoryMovementId = movement.InventoryMovementId,
                }
                : item)
            .ToArray();
        return new ListCountExecutionsResponse(projectedItems, total);
    }
}

public sealed record ListWcsTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ExternalTaskId,
    WarehouseTaskId? WarehouseTaskId = null,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    bool? Failed = null,
    string? Keyword = null) : IQuery<ListWcsTasksResponse>;

public sealed record ListWcsTasksResponse(IReadOnlyCollection<WcsTaskFact> Items, int Total);

public sealed record WcsTaskFact(
    WcsTaskId WcsTaskId,
    string OrganizationId,
    string EnvironmentId,
    WarehouseTaskId WarehouseTaskId,
    string AdapterType,
    string ExternalTaskId,
    string Status,
    int AttemptCount,
    string? FailureCode,
    string? FailureMessage,
    DateTime DispatchedAtUtc,
    DateTime? FailedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ListWcsDispatchCircuitsQuery(string OrganizationId, string EnvironmentId) : IQuery<IReadOnlyCollection<WcsDispatchCircuitFact>>;

public sealed record WcsDispatchCircuitFact(string AdapterType, string DeviceId, int ConsecutiveFailureCount, bool IsOpen, DateTime? OpenedAtUtc, DateTime? LastFailureAtUtc, DateTime? ResetAtUtc);

public sealed class ListWcsDispatchCircuitsQueryHandler(ApplicationDbContext dbContext) : IQueryHandler<ListWcsDispatchCircuitsQuery, IReadOnlyCollection<WcsDispatchCircuitFact>>
{
    public async Task<IReadOnlyCollection<WcsDispatchCircuitFact>> Handle(ListWcsDispatchCircuitsQuery request, CancellationToken cancellationToken) =>
        await dbContext.WcsDispatchCircuits.AsNoTracking().Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderBy(x => x.AdapterType).ThenBy(x => x.DeviceId)
            .Select(x => new WcsDispatchCircuitFact(x.AdapterType, x.DeviceId, x.ConsecutiveFailureCount, x.OpenedAtUtc != null, x.OpenedAtUtc, x.LastFailureAtUtc, x.ResetAtUtc))
            .ToArrayAsync(cancellationToken);
}

public sealed class ListWcsTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWcsTasksQuery, ListWcsTasksResponse>
{
    public async Task<ListWcsTasksResponse> Handle(ListWcsTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.WcsTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.ExternalTaskId))
        {
            query = query.Where(x => x.ExternalTaskId == request.ExternalTaskId);
        }

        if (request.WarehouseTaskId is not null)
        {
            query = query.Where(x => x.WarehouseTaskId == request.WarehouseTaskId);
        }

        if (WmsListQueryFilters.TryParseStatus<WcsTaskStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListWcsTasksResponse([], 0);
        }

        if (request.Failed is true)
        {
            query = query.Where(x => x.FailedAtUtc != null);
        }
        else if (request.Failed is false)
        {
            query = query.Where(x => x.FailedAtUtc == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x => x.ExternalTaskId.ToUpper().Contains(keyword));
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.DispatchedAtUtc)
            .ThenByDescending(x => x.ExternalTaskId)
            .Skip(skip)
            .Take(take)
            .Select(x => new WcsTaskFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.WarehouseTaskId,
                x.AdapterType,
                x.ExternalTaskId,
                x.Status.ToString(),
                x.AttemptCount,
                x.FailureCode,
                x.FailureMessage,
                x.DispatchedAtUtc,
                x.FailedAtUtc,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListWcsTasksResponse(items, total);
    }
}

public sealed record ListReceivingQualityGatesQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? GateStatus = null,
    string? Keyword = null,
    bool IncludeNotRequired = false,
    // 精确单号过滤：PDA 收货明细按单取完整行，避免 keyword（同时命中 SKU/检验号）
    // 跨单串扰。与 keyword 互补——keyword 用于列表模糊搜。
    string? InboundOrderNo = null,
    IReadOnlyCollection<string>? AssignedOperatorUserIds = null,
    IReadOnlyCollection<string>? AssignedPoolCodes = null,
    IReadOnlyCollection<string>? SiteCodes = null,
    bool SiteWideScope = false) : IQuery<ListReceivingQualityGatesResponse>;

public sealed record ListReceivingQualityGatesResponse(IReadOnlyCollection<ReceivingQualityGateFact> Items, int Total);

public sealed record ReceivingQualityGateFact(
    InboundOrderId InboundOrderId,
    InboundOrderLineId InboundOrderLineId,
    string OrganizationId,
    string EnvironmentId,
    string InboundOrderNo,
    string InboundOrderStatus,
    string SiteCode,
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string StagingLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string QualityGateStatus,
    string? InspectionRecordId,
    string? QualityDispositionReason,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    DateTime CreatedAtUtc);

public sealed class ListReceivingQualityGatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListReceivingQualityGatesQuery, ListReceivingQualityGatesResponse>
{
    public async Task<ListReceivingQualityGatesResponse> Handle(ListReceivingQualityGatesQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId)
            || string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return new ListReceivingQualityGatesResponse([], 0);
        }

        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var orderQuery = dbContext.InboundOrders
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        if (!WmsOwnershipQueryFilters.TryResolve(
                request.AssignedOperatorUserIds,
                request.AssignedPoolCodes,
                request.SiteCodes,
                WmsOwnershipQueryFilters.ModeOf(
                    organizationWideScope: false,
                    request.SiteWideScope),
                out var ownershipScope))
        {
            return new ListReceivingQualityGatesResponse([], 0);
        }

        var siteCodes = WmsOwnershipQueryFilters.Normalize(request.SiteCodes);
        if (siteCodes.Length == 0)
        {
            return new ListReceivingQualityGatesResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.InboundOrderNo))
        {
            var exactOrder = await orderQuery
                .Where(x => x.InboundOrderNo == request.InboundOrderNo)
                .Select(x => new
                {
                    x.AssignedOperatorUserId,
                    x.AssignedPoolCode,
                    x.SiteCode,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (exactOrder is not null)
            {
                var ownershipMatches = ownershipScope.Kind switch
                {
                    WmsOwnershipScopeKind.Operator =>
                        exactOrder.AssignedOperatorUserId is not null
                        && ownershipScope.Values.Contains(exactOrder.AssignedOperatorUserId),
                    WmsOwnershipScopeKind.Pool =>
                        exactOrder.AssignedPoolCode is not null
                        && ownershipScope.Values.Contains(exactOrder.AssignedPoolCode),
                    WmsOwnershipScopeKind.Site => true,
                    _ => false,
                };
                if (!ownershipMatches || !siteCodes.Contains(exactOrder.SiteCode))
                {
                    throw WmsAuthorizationException.Forbidden(
                        "resource-outside-selected-work-scope");
                }
            }
        }

        orderQuery = ownershipScope.Kind switch
        {
            WmsOwnershipScopeKind.Operator => orderQuery.Where(x =>
                x.AssignedOperatorUserId != null
                && ownershipScope.Values.Contains(x.AssignedOperatorUserId)),
            WmsOwnershipScopeKind.Pool => orderQuery.Where(x =>
                x.AssignedPoolCode != null
                && ownershipScope.Values.Contains(x.AssignedPoolCode)),
            WmsOwnershipScopeKind.Site => orderQuery,
            _ => orderQuery.Where(_ => false),
        };
        orderQuery = orderQuery.Where(x => siteCodes.Contains(x.SiteCode));
        var query = orderQuery.SelectMany(
            order => order.Lines,
            (order, line) => new { order, line });

        // 默认仅质检工作清单（排除免检行）；IncludeNotRequired=true 时返回全部收货行，
        // 供 PDA 收货明细展示/采集免检行的批号效期与「免检」状态标。
        if (!request.IncludeNotRequired)
        {
            query = query.Where(x => x.line.QualityGateStatus != InboundQualityGateStatuses.NotRequired);
        }

        if (!string.IsNullOrWhiteSpace(request.InboundOrderNo))
        {
            // 精确单号：按单取该单的完整收货行（无跨单串扰）。
            query = query.Where(x => x.order.InboundOrderNo == request.InboundOrderNo);
        }

        if (!string.IsNullOrWhiteSpace(request.GateStatus))
        {
            var gateStatus = request.GateStatus.Trim().ToLowerInvariant();
            query = query.Where(x => x.line.QualityGateStatus == gateStatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.order.InboundOrderNo.ToUpper().Contains(keyword)
                || x.line.SkuCode.ToUpper().Contains(keyword)
                || (x.line.InspectionRecordId != null && x.line.InspectionRecordId.ToUpper().Contains(keyword)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.order.CreatedAtUtc)
            .ThenByDescending(x => x.order.InboundOrderNo)
            .ThenBy(x => x.line.LineNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new ReceivingQualityGateFact(
                x.order.Id,
                x.line.Id,
                x.order.OrganizationId,
                x.order.EnvironmentId,
                x.order.InboundOrderNo,
                x.order.Status.ToString(),
                x.order.SiteCode,
                x.line.LineNo,
                x.line.SkuCode,
                x.line.UomCode,
                x.line.ReceivedQuantity,
                x.line.StagingLocationCode,
                x.line.LotNo,
                x.line.SerialNo,
                x.line.QualityStatus,
                x.line.QualityGateStatus,
                x.line.InspectionRecordId,
                x.line.QualityDispositionReason,
                x.line.OwnerType,
                x.line.OwnerId,
                x.line.ProductionDate,
                x.line.ExpiryDate,
                x.order.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListReceivingQualityGatesResponse(items, total);
    }
}

public sealed record ListSupplierReturnRequestsQuery(
    string? OrganizationId,
    string? EnvironmentId,
    int Skip = 0,
    int Take = 100,
    string? Status = null,
    string? Keyword = null) : IQuery<ListSupplierReturnRequestsResponse>;

public sealed record ListSupplierReturnRequestsResponse(IReadOnlyCollection<SupplierReturnRequestFact> Items, int Total);

public sealed record SupplierReturnRequestFact(
    SupplierReturnRequestId SupplierReturnRequestId,
    string OrganizationId,
    string EnvironmentId,
    string SupplierReturnNo,
    string InboundOrderNo,
    string InboundOrderLineNo,
    string InspectionRecordId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    string DispositionType,
    string? DispositionReason,
    string Status,
    DateTime CreatedAtUtc);

public sealed class ListSupplierReturnRequestsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSupplierReturnRequestsQuery, ListSupplierReturnRequestsResponse>
{
    public async Task<ListSupplierReturnRequestsResponse> Handle(ListSupplierReturnRequestsQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500);
        var query = dbContext.SupplierReturnRequests
            .AsNoTracking()
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        if (WmsListQueryFilters.TryParseStatus<SupplierReturnRequestStatus>(request.Status, out var status))
        {
            query = query.Where(x => x.Status == status);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status))
        {
            return new ListSupplierReturnRequestsResponse([], 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = WmsListQueryFilters.NormalizeKeyword(request.Keyword);
            query = query.Where(x =>
                x.SupplierReturnNo.ToUpper().Contains(keyword)
                || x.InboundOrderNo.ToUpper().Contains(keyword)
                || x.InspectionRecordId.ToUpper().Contains(keyword)
                || x.SkuCode.ToUpper().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.SupplierReturnNo)
            .Skip(skip)
            .Take(take)
            .Select(x => new SupplierReturnRequestFact(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.SupplierReturnNo,
                x.InboundOrderNo,
                x.InboundOrderLineNo,
                x.InspectionRecordId,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.OwnerType,
                x.OwnerId,
                x.Quantity,
                x.DispositionType,
                x.DispositionReason,
                x.Status.ToString(),
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ListSupplierReturnRequestsResponse(items, total);
    }
}

internal static class WmsListQueryFilters
{
    public static bool TryParseStatus<TStatus>(string? value, out TStatus status)
        where TStatus : struct, Enum
    {
        status = default;
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        return Enum.TryParse(trimmed, true, out status) && Enum.IsDefined(status);
    }

    public static string NormalizeKeyword(string? value)
    {
        return value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}
