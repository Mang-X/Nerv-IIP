using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Web.Application.Queries;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.AppHub.Web.Endpoints.Instances;

[HttpPost("/internal/apphub/v1/instances/query")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class QueryInstancesEndpoint(IMediator mediator) : Endpoint<InstanceListQuery, ResponseData<InstanceListResponse>>
{
    public override async Task HandleAsync(InstanceListQuery req, CancellationToken ct)
    {
        var response = await mediator.Send(new ListApplicationInstancesQuery(req), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}

[HttpGet("/internal/apphub/v1/instances/{instanceKey}")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetInstanceDetailEndpoint(IMediator mediator) : EndpointWithoutRequest<ResponseData<InstanceDetailResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var instanceKey = Route<string>("instanceKey")!;
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        var response = await mediator.Send(new GetApplicationInstanceDetailQuery(organizationId, environmentId, instanceKey), ct);
        await Send.OkAsync(response.AsResponseData(), ct);
    }
}
