using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Web.Application.Queries;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Ops.Web.Endpoints.AuditRecords;

public sealed record ListAuditRecordsRequest(
    string OrganizationId,
    string EnvironmentId,
    string? OperationTaskId);

[HttpGet("/api/ops/v1/audit-records")]
[AllowAnonymous]
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
