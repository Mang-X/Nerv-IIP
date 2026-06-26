using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Web.Application.Queries;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Ops.Web.Endpoints.AuditRecords;

public sealed record ListAuditRecordsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? OperationTaskId);

public sealed record ValidateAuditIntegrityRequest(
    string OrganizationId,
    string EnvironmentId);

[HttpGet("/api/ops/v1/audit-records")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ListAuditRecordsEndpoint(IMediator mediator)
    : Endpoint<ListAuditRecordsRequest, ResponseData<AuditRecordListResponse>>
{
    public override async Task HandleAsync(ListAuditRecordsRequest req, CancellationToken ct)
    {
        var records = await mediator.Send(new ListAuditRecordsQuery(
            req.OrganizationId,
            req.EnvironmentId,
            req.OperationTaskId), ct);
        await Send.OkAsync(records.AsResponseData(), ct);
    }
}

[HttpGet("/api/ops/v1/audit-records/integrity")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class ValidateAuditIntegrityEndpoint(IMediator mediator)
    : Endpoint<ValidateAuditIntegrityRequest, ResponseData<AuditIntegrityValidationResponse>>
{
    public override async Task HandleAsync(ValidateAuditIntegrityRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new ValidateAuditIntegrityQuery(
            req.OrganizationId,
            req.EnvironmentId), ct);
        await Send.OkAsync(result.AsResponseData(), ct);
    }
}
