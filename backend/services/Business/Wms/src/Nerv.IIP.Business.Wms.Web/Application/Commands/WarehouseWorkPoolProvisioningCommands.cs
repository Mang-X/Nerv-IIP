using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

/// <summary>
/// 现场作业池的写面结果。<c>Created</c> 区分「本次新建」与「已存在、按幂等返回」，
/// 让调用方（含验收脚本）能如实断言自己造出来的夹具，而不是猜。
/// </summary>
public sealed record WarehouseWorkPoolProvisionResult(
    string PoolCode,
    string DisplayName,
    string SiteCode,
    bool Active,
    bool Created);

public sealed record WarehouseWorkPoolMemberResult(
    string PoolCode,
    string PrincipalId,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool Created);

/// <summary>
/// 建立（或幂等复用）一个现场作业池。
///
/// 边界与 <see cref="Auth.WarehouseWorkScopeAuthorizer.AuthorizeAssignmentAsync"/> 同一口径：
/// 作业池归属站点，站点必须落在调用方的 IAM 精确站点授权内；WMS 不自建站点主数据，
/// 因此站点合法性只由授权集合成立，不额外查表。
/// </summary>
public sealed record ProvisionWarehouseWorkPoolCommand(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string DisplayName,
    string SiteCode) : ICommand<WarehouseWorkPoolProvisionResult>;

public sealed class ProvisionWarehouseWorkPoolCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ProvisionWarehouseWorkPoolCommand, WarehouseWorkPoolProvisionResult>
{
    public async Task<WarehouseWorkPoolProvisionResult> Handle(
        ProvisionWarehouseWorkPoolCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = WarehouseWorkPoolProvisioningText.Required(
            request.OrganizationId,
            nameof(request.OrganizationId));
        var environmentId = WarehouseWorkPoolProvisioningText.Required(
            request.EnvironmentId,
            nameof(request.EnvironmentId));
        var poolCode = WarehouseWorkPoolProvisioningText.Required(
            request.PoolCode,
            nameof(request.PoolCode));
        var displayName = WarehouseWorkPoolProvisioningText.Required(
            request.DisplayName,
            nameof(request.DisplayName));
        var siteCode = WarehouseWorkPoolProvisioningText.Required(
            request.SiteCode,
            nameof(request.SiteCode));
        WarehouseWorkPoolProvisioningText.EnsureSiteAuthorized(request.AuthorizedSiteCodes, siteCode);

        var existing = await dbContext.WarehouseWorkPools.SingleOrDefaultAsync(
            pool => pool.OrganizationId == organizationId
                && pool.EnvironmentId == environmentId
                && pool.PoolCode == poolCode,
            cancellationToken);
        if (existing is not null)
        {
            // 同一池码换站点属于跨站点改写资格边界，不做静默覆盖。
            if (!string.Equals(existing.SiteCode, siteCode, StringComparison.Ordinal))
            {
                throw WmsAuthorizationException.Forbidden("work-pool-site-mismatch");
            }

            return new WarehouseWorkPoolProvisionResult(
                existing.PoolCode,
                existing.DisplayName,
                existing.SiteCode,
                existing.Active,
                Created: false);
        }

        var pool = WarehouseWorkPool.Create(
            organizationId,
            environmentId,
            poolCode,
            displayName,
            siteCode);
        dbContext.WarehouseWorkPools.Add(pool);
        return new WarehouseWorkPoolProvisionResult(
            pool.PoolCode,
            pool.DisplayName,
            pool.SiteCode,
            pool.Active,
            Created: true);
    }
}

/// <summary>
/// 给现场作业池加一名有效成员（幂等）。成员资格是现场作业资格：
/// 只有池内有效成员才能成为派工的被指派人。
/// </summary>
public sealed record AddWarehouseWorkPoolMemberCommand(
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string PoolCode,
    string PrincipalId,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc) : ICommand<WarehouseWorkPoolMemberResult>;

public sealed class AddWarehouseWorkPoolMemberCommandHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<AddWarehouseWorkPoolMemberCommand, WarehouseWorkPoolMemberResult>
{
    public async Task<WarehouseWorkPoolMemberResult> Handle(
        AddWarehouseWorkPoolMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = WarehouseWorkPoolProvisioningText.Required(
            request.OrganizationId,
            nameof(request.OrganizationId));
        var environmentId = WarehouseWorkPoolProvisioningText.Required(
            request.EnvironmentId,
            nameof(request.EnvironmentId));
        var poolCode = WarehouseWorkPoolProvisioningText.Required(
            request.PoolCode,
            nameof(request.PoolCode));
        var principalId = WarehouseWorkPoolProvisioningText.Required(
            request.PrincipalId,
            nameof(request.PrincipalId));

        var pool = await dbContext.WarehouseWorkPools.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == organizationId
                && candidate.EnvironmentId == environmentId
                && candidate.PoolCode == poolCode
                && candidate.Active,
            cancellationToken)
            ?? throw WmsAuthorizationException.Forbidden("inactive-or-unknown-work-pool");
        WarehouseWorkPoolProvisioningText.EnsureSiteAuthorized(
            request.AuthorizedSiteCodes,
            pool.SiteCode);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var effectiveFromUtc = WarehouseWorkPoolProvisioningText.EnsureUtc(
            request.EffectiveFromUtc ?? now);
        var effectiveToUtc = request.EffectiveToUtc is null
            ? (DateTime?)null
            : WarehouseWorkPoolProvisioningText.EnsureUtc(request.EffectiveToUtc.Value);
        if (effectiveToUtc is not null && effectiveToUtc <= effectiveFromUtc)
        {
            throw new WmsUnprocessableException(
                "membership-window-not-forward",
                "membership-window-not-forward");
        }

        var existing = await dbContext.WarehouseWorkPoolMemberships
            .Where(membership => membership.OrganizationId == organizationId
                && membership.EnvironmentId == environmentId
                && membership.PoolCode == poolCode
                && membership.PrincipalId == principalId
                && membership.Active
                && membership.EffectiveFromUtc <= now
                && (membership.EffectiveToUtc == null || now < membership.EffectiveToUtc))
            .OrderBy(membership => membership.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new WarehouseWorkPoolMemberResult(
                existing.PoolCode,
                existing.PrincipalId,
                existing.EffectiveFromUtc,
                existing.EffectiveToUtc,
                Created: false);
        }

        var membership = WarehouseWorkPoolMembership.Create(
            organizationId,
            environmentId,
            poolCode,
            principalId,
            effectiveFromUtc,
            effectiveToUtc);
        dbContext.WarehouseWorkPoolMemberships.Add(membership);
        return new WarehouseWorkPoolMemberResult(
            membership.PoolCode,
            membership.PrincipalId,
            membership.EffectiveFromUtc,
            membership.EffectiveToUtc,
            Created: true);
    }
}

internal static class WarehouseWorkPoolProvisioningText
{
    public static string Required(string? value, string parameterName)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : normalized;
    }

    public static void EnsureSiteAuthorized(
        IReadOnlyCollection<string>? authorizedSiteCodes,
        string siteCode)
    {
        var authorized = authorizedSiteCodes?
            .Select(code => code?.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.Ordinal)
            ?? [];
        if (authorized.Count == 0)
        {
            throw WmsAuthorizationException.Forbidden("missing-exact-site-grant");
        }

        if (!authorized.Contains(siteCode))
        {
            throw WmsAuthorizationException.Forbidden("site-outside-exact-grant");
        }
    }

    public static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
