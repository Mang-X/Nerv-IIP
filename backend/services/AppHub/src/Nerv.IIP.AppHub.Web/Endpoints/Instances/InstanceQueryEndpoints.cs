using FastEndpoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Web.Application.Queries;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.AppHub.Web.Endpoints.Instances;

[HttpPost("/internal/apphub/v1/instances/query")]
[AllowAnonymous]
public sealed class QueryInstancesEndpoint(IMediator mediator) : Endpoint<InstanceListQuery>
{
    public override async Task HandleAsync(InstanceListQuery req, CancellationToken ct)
    {
        var response = await mediator.Send(new ListApplicationInstancesQuery(req), ct);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

[HttpGet("/internal/apphub/v1/instances/{instanceKey}")]
[AllowAnonymous]
public sealed class GetInstanceDetailEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var instanceKey = Route<string>("instanceKey")!;
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        var response = await mediator.Send(new GetApplicationInstanceDetailQuery(organizationId, environmentId, instanceKey), ct);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}
