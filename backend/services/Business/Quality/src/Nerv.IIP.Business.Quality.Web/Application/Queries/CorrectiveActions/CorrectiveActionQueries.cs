using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.CorrectiveActionAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.CorrectiveActions;

/// <summary>CAPA 明细项（8D/PDCA 的一步：临时措施 / 纠正措施 / 预防措施）。</summary>
public sealed record CorrectiveActionItemResponse(
    CorrectiveActionItemId CorrectiveActionItemId,
    string ActionType,
    string Description,
    string OwnerUserId,
    DateTimeOffset DueAtUtc,
    string Status,
    string? CompletedByUserId,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    bool Overdue);

public sealed record CorrectiveActionResponse(
    CorrectiveActionId CorrectiveActionId,
    string OrganizationId,
    string EnvironmentId,
    string CapaCode,
    string? SourceNcrId,
    string RootCause,
    string ContainmentAction,
    string OwnerUserId,
    DateTimeOffset DueAtUtc,
    string Status,
    string? EffectivenessVerifiedByUserId,
    string? EffectivenessResult,
    DateTimeOffset? EffectivenessVerifiedAtUtc,
    string? EffectivenessInspectionRecordId,
    string? CloseApprovalChainId,
    string? ClosedByUserId,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int ActionCount,
    int CompletedActionCount,
    bool Overdue,
    IReadOnlyCollection<CorrectiveActionItemResponse> Actions,
    // SourceNcrCode：来源 NCR 的**单号**（NCR-2026-0001）。SourceNcrId 是聚合 GUID，界面上
    // 「来源 NCR」列直接渲染它就是一串裸 GUID；前端没有 NCR 名录可查，必须由读面回带人读单号。
    // 源 NCR 已被清理时为 null。
    string? SourceNcrCode = null);

public sealed record ListCorrectiveActionsResponse(
    IReadOnlyCollection<CorrectiveActionResponse> Items,
    int Total,
    int OpenCount,
    int EffectivenessVerifiedCount,
    int ClosedCount,
    int OverdueCount);

/// <summary>
/// CAPA 台账读面。
///
/// CAPA 聚合此前只有写端点（开单 / 加措施 / 完成措施 / 效果验证 / 关单），
/// 于是「纠正措施」这件事在系统里只能写不能看。本查询把 CAPA 与其明细项一起返回：
/// 明细项数量本来就小（一张 CAPA 4–6 步），拆两跳查询反而让页面要么 N+1 要么展不开。
/// </summary>
public sealed record ListCorrectiveActionsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? OwnerUserId = null,
    string? SourceNcrId = null,
    bool? OverdueOnly = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListCorrectiveActionsResponse>;

/// <summary>
/// 按 id 取单条 CAPA（含明细项）。<paramref name="OrganizationId"/> / <paramref name="EnvironmentId"/>
/// 提供时按租户过滤——与 NCR 详情同口径：越权 id 与不存在同样 not found。
/// </summary>
public sealed record GetCorrectiveActionQuery(
    CorrectiveActionId CorrectiveActionId,
    string? OrganizationId = null,
    string? EnvironmentId = null) : IQuery<CorrectiveActionResponse>;

public sealed class GetCorrectiveActionQueryValidator : AbstractValidator<GetCorrectiveActionQuery>
{
    public GetCorrectiveActionQueryValidator()
    {
        RuleFor(x => x.CorrectiveActionId).NotEmpty();
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
    }
}

public sealed class ListCorrectiveActionsQueryValidator : AbstractValidator<ListCorrectiveActionsQuery>
{
    public ListCorrectiveActionsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Status).MaximumLength(50);
        RuleFor(x => x.OwnerUserId).MaximumLength(100);
        RuleFor(x => x.SourceNcrId).MaximumLength(100);
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListCorrectiveActionsQueryHandler(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<ListCorrectiveActionsQuery, ListCorrectiveActionsResponse>
{
    public async Task<ListCorrectiveActionsResponse> Handle(
        ListCorrectiveActionsQuery request,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var take = Math.Clamp(request.Take, 1, 500);
        var baseQuery = dbContext.CorrectiveActions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var ownerUserId = request.OwnerUserId.Trim();
            baseQuery = baseQuery.Where(x => x.OwnerUserId == ownerUserId);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceNcrId))
        {
            var sourceNcrId = request.SourceNcrId.Trim();
            baseQuery = baseQuery.Where(x => x.SourceNcrId == sourceNcrId);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            baseQuery = baseQuery.Where(x =>
                x.CapaCode.ToLower().Contains(keyword)
                || x.RootCause.ToLower().Contains(keyword)
                || x.OwnerUserId.ToLower().Contains(keyword));
        }

        var openCount = await baseQuery.CountAsync(x => x.Status == CorrectiveActionStatuses.Open, cancellationToken);
        var effectivenessVerifiedCount = await baseQuery.CountAsync(
            x => x.Status == CorrectiveActionStatuses.EffectivenessVerified, cancellationToken);
        var closedCount = await baseQuery.CountAsync(x => x.Status == CorrectiveActionStatuses.Closed, cancellationToken);
        var overdueCount = await baseQuery.CountAsync(
            x => x.Status != CorrectiveActionStatuses.Closed && x.DueAtUtc < now, cancellationToken);

        var filtered = baseQuery;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            filtered = filtered.Where(x => x.Status == status);
        }

        if (request.OverdueOnly == true)
        {
            filtered = filtered.Where(x => x.Status != CorrectiveActionStatuses.Closed && x.DueAtUtc < now);
        }

        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            .Include(x => x.Actions)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.CapaCode)
            .Skip(request.Skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var sourceNcrCodes = await CorrectiveActionProjection.ResolveSourceNcrCodesAsync(dbContext, rows, cancellationToken);

        return new ListCorrectiveActionsResponse(
            [.. rows.Select(row => CorrectiveActionProjection.ToResponse(row, now, sourceNcrCodes))],
            total,
            openCount,
            effectivenessVerifiedCount,
            closedCount,
            overdueCount);
    }
}

public sealed class GetCorrectiveActionQueryHandler(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetCorrectiveActionQuery, CorrectiveActionResponse>
{
    public async Task<CorrectiveActionResponse> Handle(
        GetCorrectiveActionQuery request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CorrectiveActions
            .AsNoTracking()
            .Include(x => x.Actions)
            .Where(x => x.Id == request.CorrectiveActionId);

        // org/env 由网关 facade 必传：越权与不存在一律 not found，不泄漏跨租户存在性。
        if (!string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            var organizationId = request.OrganizationId.Trim();
            query = query.Where(x => x.OrganizationId == organizationId);
        }

        if (!string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            var environmentId = request.EnvironmentId.Trim();
            query = query.Where(x => x.EnvironmentId == environmentId);
        }

        var capa = await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"找不到 CAPA {request.CorrectiveActionId}，请在 CAPA 页面刷新并确认编号后重试。");
        var sourceNcrCodes = await CorrectiveActionProjection.ResolveSourceNcrCodesAsync(dbContext, [capa], cancellationToken);
        return CorrectiveActionProjection.ToResponse(capa, timeProvider.GetUtcNow(), sourceNcrCodes);
    }
}

/// <summary>CAPA 领域状态字面量（领域层用裸字符串，读面在此集中一次，避免各处再手抄）。</summary>
public static class CorrectiveActionStatuses
{
    public const string Open = "open";
    public const string EffectivenessVerified = "effectiveness-verified";
    public const string Closed = "closed";
}

internal static class CorrectiveActionProjection
{
    /// <summary>
    /// 批量解析 CAPA 的来源 NCR 单号。<c>SourceNcrId</c> 存的是 NCR 聚合 GUID 的字符串形式，
    /// 这里一次查回本页涉及的 NCR，避免逐行 N+1；解析不出的 id（脏数据/已清理）直接跳过，
    /// 读面回 null 而不是抛。
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> ResolveSourceNcrCodesAsync(
        ApplicationDbContext dbContext,
        IEnumerable<CorrectiveAction> capas,
        CancellationToken cancellationToken)
    {
        var ncrIds = capas
            .Select(x => x.SourceNcrId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Select(x => Guid.TryParse(x, out var parsed) ? new NonconformanceReportId(parsed) : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        if (ncrIds.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var rows = await dbContext.NonconformanceReports
            .AsNoTracking()
            .Where(x => ncrIds.Contains(x.Id))
            .Select(x => new { x.Id, x.NcrCode })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id.ToString()!, x => x.NcrCode, StringComparer.Ordinal);
    }

    public static CorrectiveActionResponse ToResponse(
        CorrectiveAction capa,
        DateTimeOffset nowUtc,
        IReadOnlyDictionary<string, string>? sourceNcrCodes = null)
    {
        var actions = capa.Actions
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.ActionType, StringComparer.Ordinal)
            .Select(x => new CorrectiveActionItemResponse(
                x.Id,
                x.ActionType,
                x.Description,
                x.OwnerUserId,
                x.DueAtUtc,
                x.Status,
                x.CompletedByUserId,
                x.CompletedAtUtc,
                x.CreatedAtUtc,
                x.Status != "completed" && x.DueAtUtc < nowUtc))
            .ToArray();

        return new CorrectiveActionResponse(
            capa.Id,
            capa.OrganizationId,
            capa.EnvironmentId,
            capa.CapaCode,
            capa.SourceNcrId,
            capa.RootCause,
            capa.ContainmentAction,
            capa.OwnerUserId,
            capa.DueAtUtc,
            capa.Status,
            capa.EffectivenessVerifiedByUserId,
            capa.EffectivenessResult,
            capa.EffectivenessVerifiedAtUtc,
            capa.EffectivenessInspectionRecordId?.ToString(),
            capa.CloseApprovalChainId,
            capa.ClosedByUserId,
            capa.ClosedAtUtc,
            capa.CreatedAtUtc,
            capa.UpdatedAtUtc,
            actions.Length,
            actions.Count(x => x.Status == "completed"),
            capa.Status != CorrectiveActionStatuses.Closed && capa.DueAtUtc < nowUtc,
            actions,
            ResolveCode(capa.SourceNcrId, sourceNcrCodes));
    }

    private static string? ResolveCode(string? sourceNcrId, IReadOnlyDictionary<string, string>? codes) =>
        sourceNcrId is not null && codes is not null && codes.TryGetValue(sourceNcrId, out var code)
            ? code
            : null;
}
