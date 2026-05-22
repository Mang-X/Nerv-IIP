using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;
using Nerv.IIP.Ops.Web.Application.Commands;
using Nerv.IIP.Ops.Web.Endpoints.OperationTasks;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Ops.Web.Endpoints.AuditIntents;

[HttpPost("/api/ops/v1/audit-intents")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class SubmitAuditIntentEndpoint(IMediator mediator) : Endpoint<SubmitAuditIntentRequest, ResponseData<AuditIntentResponse>>
{
    public override async Task HandleAsync(SubmitAuditIntentRequest req, CancellationToken ct)
    {
        try
        {
            var intent = await mediator.Send(new SubmitAuditIntentCommand(req, DateTimeOffset.UtcNow), ct);
            await Send.OkAsync(intent.AsResponseData(), ct);
        }
        catch (InvalidOperationTaskRequestException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
        catch (OperationTaskNotFoundException ex)
        {
            await OpsEndpointResults.WriteBadRequestAsync(HttpContext, ex.Message, ct);
        }
    }
}
