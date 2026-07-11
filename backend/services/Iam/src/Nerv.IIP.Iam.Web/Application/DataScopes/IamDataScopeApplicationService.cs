using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Infrastructure.Repositories;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using NetCorePal.Extensions.Primitives;
using static Nerv.IIP.Iam.Web.Application.DataScopes.DataScopeApplicationMapping;

namespace Nerv.IIP.Iam.Web.Application.DataScopes;

public sealed record DataScopeBindingRequest(string ScopeType, string ScopeCode);
public sealed record DataScopeResponse(string ScopeType, string ScopeCode);
public sealed record DataScopeListResponse(IReadOnlyList<DataScopeResponse> DataScopes);
public sealed record PatchDataScopesRequest(IReadOnlyList<DataScopeBindingRequest> DataScopes);
public sealed record PatchMembershipDataScopesRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyList<DataScopeBindingRequest> DataScopes);

public interface IIamDataScopeApplicationService
{
    Task<DataScopeListResponse> PatchRoleDataScopesAsync(
        string roleId,
        IReadOnlyList<DataScopeBindingRequest> dataScopes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken);

    Task<DataScopeListResponse> PatchMembershipDataScopesAsync(
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyList<DataScopeBindingRequest> dataScopes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken);
}

public sealed class InMemoryIamDataScopeApplicationService(InMemoryIamStore store) : IIamDataScopeApplicationService
{
    public Task<DataScopeListResponse> PatchRoleDataScopesAsync(
        string roleId,
        IReadOnlyList<DataScopeBindingRequest> dataScopes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        _ = auditContext;
        _ = cancellationToken;
        try
        {
            return Task.FromResult(ToResponse(store.ReplaceRoleDataScopes(roleId, Normalize(dataScopes))));
        }
        catch (ArgumentException ex)
        {
            throw new KnownException(ex.Message);
        }
        catch (InvalidOperationException)
        {
            throw new KnownException($"Role '{roleId}' was not found.");
        }
    }

    public Task<DataScopeListResponse> PatchMembershipDataScopesAsync(
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyList<DataScopeBindingRequest> dataScopes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        _ = auditContext;
        _ = cancellationToken;
        try
        {
            return Task.FromResult(ToResponse(store.ReplaceMembershipDataScopes(userId, organizationId, environmentId, Normalize(dataScopes))));
        }
        catch (ArgumentException ex)
        {
            throw new KnownException(ex.Message);
        }
        catch (InvalidOperationException)
        {
            throw new KnownException($"Membership for user '{userId}' in '{organizationId}/{environmentId}' was not found.");
        }
    }
}

public sealed class PostgreSqlIamDataScopeApplicationService(
    IRoleRepository roleRepository,
    IMembershipRepository membershipRepository,
    ISecurityAuditRecorder securityAudit) : IIamDataScopeApplicationService
{
    public async Task<DataScopeListResponse> PatchRoleDataScopesAsync(
        string roleId,
        IReadOnlyList<DataScopeBindingRequest> dataScopes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetByIdAsync(new RoleId(roleId), cancellationToken)
            ?? throw new KnownException($"Role '{roleId}' was not found.");
        var normalized = Normalize(dataScopes);
        var before = role.DataScopes.Select(x => new DataScopeBinding(x.ScopeType, x.ScopeCode)).ToArray();
        role.ReplaceDataScopes(normalized);
        var after = role.DataScopes.Select(x => new DataScopeBinding(x.ScopeType, x.ScopeCode)).ToArray();
        await securityAudit.RecordAsync(
            auditContext ?? UnknownAuditContext(),
            "iam.role.data-scopes.changed",
            "role",
            role.Id.Id,
            "success",
            new { before = ToResponseItems(before), after = ToResponseItems(after) },
            DateTimeOffset.UtcNow,
            cancellationToken);
        return ToResponse(after);
    }

    public async Task<DataScopeListResponse> PatchMembershipDataScopesAsync(
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyList<DataScopeBindingRequest> dataScopes,
        SecurityAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        var membership = await membershipRepository.GetByUserIdAndOrgEnvAsync(
            new UserId(userId),
            new OrganizationId(organizationId),
            new IamEnvironmentId(environmentId),
            cancellationToken)
            ?? throw new KnownException($"Membership for user '{userId}' in '{organizationId}/{environmentId}' was not found.");
        var normalized = Normalize(dataScopes);
        var before = membership.DataScopes.Select(x => new DataScopeBinding(x.ScopeType, x.ScopeCode)).ToArray();
        membership.ReplaceDataScopes(normalized);
        var after = membership.DataScopes.Select(x => new DataScopeBinding(x.ScopeType, x.ScopeCode)).ToArray();
        await securityAudit.RecordAsync(
            auditContext ?? UnknownAuditContext(),
            "iam.membership.data-scopes.changed",
            "membership",
            membership.Id.Id,
            "success",
            new { before = ToResponseItems(before), after = ToResponseItems(after) },
            DateTimeOffset.UtcNow,
            cancellationToken);
        return ToResponse(after);
    }

    private static SecurityAuditContext UnknownAuditContext() =>
        new("unknown", Guid.CreateVersion7().ToString("N"), null, "unknown", "unknown");
}

internal static class DataScopeApplicationMapping
{
    public static IReadOnlyList<DataScopeBinding> Normalize(IReadOnlyList<DataScopeBindingRequest> dataScopes)
    {
        try
        {
            return (dataScopes ?? [])
                .Select(x => DataScopeBinding.Normalize(new DataScopeBinding(x.ScopeType, x.ScopeCode)))
                .Distinct()
                .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
                .ThenBy(x => x.ScopeCode, StringComparer.Ordinal)
                .ToArray();
        }
        catch (ArgumentException ex)
        {
            throw new KnownException(ex.Message);
        }
    }

    public static DataScopeListResponse ToResponse(IEnumerable<DataScopeBinding> scopes) =>
        new(ToResponseItems(scopes));

    public static IReadOnlyList<DataScopeResponse> ToResponseItems(IEnumerable<DataScopeBinding> scopes) =>
        scopes
            .Select(x => new DataScopeBinding(x.ScopeType.Trim(), x.ScopeCode.Trim()))
            .OrderBy(x => x.ScopeType, StringComparer.Ordinal)
            .ThenBy(x => x.ScopeCode, StringComparer.Ordinal)
            .Select(x => new DataScopeResponse(x.ScopeType, x.ScopeCode))
            .ToArray();
}
