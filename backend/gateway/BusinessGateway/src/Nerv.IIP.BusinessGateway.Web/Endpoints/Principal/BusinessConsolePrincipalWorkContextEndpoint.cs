using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Principal;

[Tags("Business Console Principal")]
[HttpGet("/api/business-console/v1/me/work-context")]
[BusinessGatewayOperationId("getBusinessConsolePrincipalWorkContext")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status502BadGateway)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status503ServiceUnavailable)]
public sealed class GetBusinessConsolePrincipalWorkContextEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : Endpoint<BusinessConsolePrincipalWorkContextRequest, ResponseData<BusinessConsolePrincipalWorkContextResponse>>
{
    public override async Task HandleAsync(BusinessConsolePrincipalWorkContextRequest req, CancellationToken ct)
    {
        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new BusinessGatewayPermissionRequirement(
                req.PermissionCode,
                req.OrganizationId,
                req.EnvironmentId,
                "principal-work-context",
                null,
                IncludePrincipalContext: true),
            BusinessGatewayAuthorizationContinuityMode.RealtimeRequired,
            ct);
        if (bearerToken is null)
        {
            return;
        }

        var authorization = HttpContext.Items[BusinessGatewayAuthorization.PrincipalItemKey]
            as BusinessGatewayAuthorizationResult;
        if (authorization is null
            || string.IsNullOrWhiteSpace(authorization.PrincipalId)
            || string.IsNullOrWhiteSpace(authorization.PrincipalType)
            || authorization.DataScope?.DenyAll == true)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status403Forbidden,
                "Forbidden.",
                ct);
            return;
        }

        try
        {
            var context = await masterData.GetPrincipalWorkContextAsync(
                tokenProvider.BearerToken,
                new BusinessMasterDataPrincipalWorkContextRequest(
                    req.OrganizationId,
                    req.EnvironmentId,
                    authorization.PrincipalId),
                ct);
            var resolution = PrincipalWorkContextAuthorizationResolver.Resolve(
                context,
                authorization,
                req.OrganizationId,
                req.PermissionCode,
                req.ScopeKind,
                req.ScopeId);
            if (!resolution.SelectionAuthorized)
            {
                await ResponseDataEndpointResults.WriteErrorAsync(
                    HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Requested work scope is not authorized.",
                    ct);
                return;
            }

            var response = new BusinessConsolePrincipalWorkContextResponse(
                req.OrganizationId,
                req.EnvironmentId,
                req.PermissionCode,
                DateTimeOffset.UtcNow,
                new BusinessConsolePrincipalIdentity(
                    authorization.PrincipalId,
                    authorization.PrincipalType,
                    authorization.LoginName,
                    NormalizeRoles(authorization.Roles ?? [])),
                context.ResolutionStatus,
                context.Worker,
                context.Teams ?? [],
                context.CoveredWorkCenters ?? [],
                context.Workshops ?? [],
                context.Shifts ?? [],
                context.Sites ?? [],
                resolution.CandidateScopes,
                resolution.CandidateScopeKinds,
                resolution.AuthorizedScopes,
                resolution.AvailableScopeKinds,
                resolution.SelectedScope,
                context.Issues ?? []);
            await ResponseDataEndpointResults.WriteDataAsync(
                HttpContext,
                StatusCodes.Status200OK,
                response,
                ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                (int)ex.StatusCode,
                ex.Message,
                ct);
        }
    }

    private static IReadOnlyCollection<Nerv.IIP.Contracts.Iam.AuthorizationRole> NormalizeRoles(
        IReadOnlyCollection<Nerv.IIP.Contracts.Iam.AuthorizationRole> roles) =>
        roles
            .Where(x =>
                x is not null
                && !string.IsNullOrWhiteSpace(x.Id)
                && !string.IsNullOrWhiteSpace(x.DisplayName))
            .GroupBy(x => x.Id.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var names = group
                    .Select(x => x.DisplayName.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return names.Length == 1
                    ? new Nerv.IIP.Contracts.Iam.AuthorizationRole(group.Key, names[0])
                    : null;
            })
            .Where(x => x is not null)
            .Cast<Nerv.IIP.Contracts.Iam.AuthorizationRole>()
            .OrderBy(x => x.DisplayName, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
}

public sealed class BusinessConsolePrincipalWorkContextRequestValidator
    : Validator<BusinessConsolePrincipalWorkContextRequest>
{
    public BusinessConsolePrincipalWorkContextRequestValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PermissionCode)
            .NotEmpty()
            .MaximumLength(200)
            .Must(BusinessGatewayPermissionCatalog.Contains)
            .WithMessage("Permission code is not supported.");
        RuleFor(x => x.ScopeKind)
            .Must(BusinessGatewayWorkScopeKinds.Contains)
            .When(x => !string.IsNullOrWhiteSpace(x.ScopeKind))
            .WithMessage("Scope kind is not supported.");
        RuleFor(x => x.ScopeId).MaximumLength(200);
        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.ScopeKind) == string.IsNullOrWhiteSpace(x.ScopeId))
            .WithMessage("Scope kind and scope id must be provided together.");
    }
}

internal static class BusinessGatewayPermissionCatalog
{
    private static readonly HashSet<string> Values = typeof(BusinessGatewayPermissions)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(x => x.IsLiteral && !x.IsInitOnly && x.FieldType == typeof(string))
        .Select(x => x.GetRawConstantValue() as string)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Cast<string>()
        .ToHashSet(StringComparer.Ordinal);

    public static bool Contains(string permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) && Values.Contains(permissionCode.Trim());
}

internal static class BusinessGatewayWorkScopeKinds
{
    private static readonly HashSet<string> Values = new(
        ["self", "team", "work-center", "workshop", "site", "organization"],
        StringComparer.Ordinal);

    public static bool Contains(string? scopeKind) =>
        !string.IsNullOrWhiteSpace(scopeKind) && Values.Contains(scopeKind.Trim());
}
