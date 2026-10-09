using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed record PrincipalWorkScopeSelection(
    string Kind,
    string Id,
    IReadOnlyCollection<string> AssignedUserIds,
    IReadOnlyCollection<string> TeamIds,
    IReadOnlyCollection<string> WorkCenterIds);

public sealed class PrincipalWorkScopeResolver(
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
{
    public async Task<PrincipalWorkScopeSelection> ResolveAsync(
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? requestedScopeKind,
        string? requestedScopeId,
        CancellationToken cancellationToken)
    {
        var (context, resolution, principalId) = await ResolveContextAsync(authorization,
            organizationId, environmentId, permissionCode, requestedScopeKind, requestedScopeId,
            cancellationToken);
        var explicitSelection = !string.IsNullOrWhiteSpace(requestedScopeKind)
            || !string.IsNullOrWhiteSpace(requestedScopeId);
        BusinessConsoleAuthorizedWorkScope selected;
        if (explicitSelection)
        {
            if (!resolution.SelectionAuthorized || resolution.SelectedScope is null)
            {
                throw Forbidden();
            }

            selected = resolution.SelectedScope;
        }
        else
        {
            var organizationScopes = resolution.AuthorizedScopes
                .Where(x => string.Equals(x.Kind, "organization", StringComparison.Ordinal)
                    && string.Equals(x.Id, organizationId, StringComparison.Ordinal))
                .ToArray();
            if (organizationScopes.Length == 1)
            {
                selected = organizationScopes[0];
            }
            else if (organizationScopes.Length > 1 || resolution.AuthorizedScopes.Count != 1)
            {
                throw Forbidden();
            }
            else
            {
                selected = resolution.AuthorizedScopes[0];
            }
        }

        return ProjectSelection(context, resolution.AuthorizedScopes, principalId, selected);
    }

    // 只有工单列表允许缺省范围取授权并集；其它入口仍使用 ResolveAsync。
    public async Task<PrincipalWorkScopeSelection> ResolveWorkOrderListAsync(
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? requestedScopeKind,
        string? requestedScopeId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedScopeKind) || !string.IsNullOrWhiteSpace(requestedScopeId))
        {
            return await ResolveAsync(authorization, organizationId, environmentId, permissionCode,
                requestedScopeKind, requestedScopeId, cancellationToken);
        }

        var (context, resolution, principalId) = await ResolveContextAsync(authorization,
            organizationId, environmentId, permissionCode, null, null, cancellationToken);
        if (resolution.AuthorizedScopes.Any(x => x.Kind == "organization" && x.Id == organizationId))
        {
            return new("organization", organizationId, [], [], []);
        }

        // 车间授权已由可信上下文展开为 work-center 候选，不把车间自身当成工序字段。
        var selections = resolution.AuthorizedScopes
            .Where(x => x.Kind != "workshop")
            .Select(scope => ProjectSelection(context, resolution.AuthorizedScopes,
                principalId, scope))
            .ToArray();
        if (selections.Length == 0)
        {
            throw Forbidden();
        }

        return new("authorized-union", principalId,
            selections.SelectMany(x => x.AssignedUserIds).Distinct(StringComparer.Ordinal).ToArray(),
            selections.SelectMany(x => x.TeamIds).Distinct(StringComparer.Ordinal).ToArray(),
            selections.SelectMany(x => x.WorkCenterIds).Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<(BusinessMasterDataPrincipalWorkContextResponse Context,
        PrincipalWorkContextAuthorizationResolution Resolution, string PrincipalId)> ResolveContextAsync(
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string environmentId,
        string permissionCode,
        string? requestedScopeKind,
        string? requestedScopeId,
        CancellationToken cancellationToken)
    {
        if (authorization is null
            || !authorization.IsAllowed
            || string.IsNullOrWhiteSpace(authorization.PrincipalId)
            || authorization.DataScope?.DenyAll == true)
        {
            throw Forbidden();
        }

        var context = await masterData.GetPrincipalWorkContextAsync(
            tokenProvider.BearerToken,
            new BusinessMasterDataPrincipalWorkContextRequest(
                organizationId,
                environmentId,
                authorization.PrincipalId),
            cancellationToken);
        var resolution = PrincipalWorkContextAuthorizationResolver.Resolve(
            context,
            authorization,
            organizationId,
            permissionCode,
            requestedScopeKind,
            requestedScopeId);
        return (context, resolution, authorization.PrincipalId);
    }

    private static PrincipalWorkScopeSelection ProjectSelection(
        BusinessMasterDataPrincipalWorkContextResponse context,
        IReadOnlyCollection<BusinessConsoleAuthorizedWorkScope> authorizedScopes,
        string principalId,
        BusinessConsoleAuthorizedWorkScope selected)
    {
        return selected.Kind switch
        {
            "self" when string.Equals(selected.Id, principalId, StringComparison.Ordinal) =>
                new(selected.Kind, selected.Id, [principalId], [], []),
            "team" => new(selected.Kind, selected.Id, [], [selected.Id], []),
            "work-center" => new(selected.Kind, selected.Id, [], [], [selected.Id]),
            "workshop" => WorkshopSelection(context, authorizedScopes, selected),
            "organization" => new(selected.Kind, selected.Id, [], [], []),
            _ => throw Forbidden(),
        };
    }

    private static PrincipalWorkScopeSelection WorkshopSelection(
        BusinessMasterDataPrincipalWorkContextResponse context,
        IReadOnlyCollection<BusinessConsoleAuthorizedWorkScope> authorizedScopes,
        BusinessConsoleAuthorizedWorkScope selected)
    {
        var authorizedWorkCenterIds = authorizedScopes
            .Where(x => string.Equals(x.Kind, "work-center", StringComparison.Ordinal))
            .Select(x => x.Id)
            .ToHashSet(StringComparer.Ordinal);
        var workCenterIds = (context.CoveredWorkCenters ?? [])
            .Where(x =>
                string.Equals(x.WorkshopId, selected.Id, StringComparison.Ordinal)
                && authorizedWorkCenterIds.Contains(x.Id))
            .Select(x => x.Id)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (workCenterIds.Length == 0)
        {
            throw Forbidden();
        }

        return new(selected.Kind, selected.Id, [], [], workCenterIds);
    }

    private static BusinessServiceProxyException Forbidden() =>
        new(HttpStatusCode.Forbidden, "work-scope-not-authorized");
}
