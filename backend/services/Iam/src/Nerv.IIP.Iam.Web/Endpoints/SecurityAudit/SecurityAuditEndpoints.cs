using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Nerv.IIP.Iam.Web.Endpoints;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Endpoints.SecurityAudit;

public sealed record ListSecurityAuditRecordsRequest(
    string? OrganizationId,
    string? EnvironmentId,
    string? Action,
    string? TargetType,
    string? TargetId,
    int? Take);

[HttpGet("/api/iam/v1/security-audit-records")]
[AllowAnonymous]
public sealed class ListSecurityAuditRecordsEndpoint(
    IIamPermissionAuthorizer authorizer,
    IIamSecurityAuditApplicationService securityAudit) : Endpoint<ListSecurityAuditRecordsRequest, ResponseData<IReadOnlyList<SecurityAuditRecordResponse>>>
{
    public override async Task HandleAsync(ListSecurityAuditRecordsRequest req, CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.security-audit.read", ct))
        {
            return;
        }

        var records = await securityAudit.ListAsync(
            new SecurityAuditListOptions(
                req.OrganizationId,
                req.EnvironmentId,
                req.Action,
                req.TargetType,
                req.TargetId,
                req.Take),
            ct);
        await Send.OkAsync(records.AsResponseData(), ct);
    }
}
