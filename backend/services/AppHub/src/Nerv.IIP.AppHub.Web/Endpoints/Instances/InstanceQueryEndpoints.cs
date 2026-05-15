using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.AppHub.Web.Endpoints.Instances;

[HttpPost("/internal/apphub/v1/instances/query")]
[AllowAnonymous]
public sealed class QueryInstancesEndpoint(InMemoryAppHubStateStore store) : Endpoint<InstanceListQuery>
{
    public override async Task HandleAsync(InstanceListQuery req, CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(store.QueryInstances(req), ct);
    }
}

[HttpGet("/internal/apphub/v1/instances/{instanceKey}")]
[AllowAnonymous]
public sealed class GetInstanceDetailEndpoint(InMemoryAppHubStateStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var instanceKey = Route<string>("instanceKey")!;
        var organizationId = Query<string>("organizationId")!;
        var environmentId = Query<string>("environmentId")!;
        await HttpContext.Response.WriteAsJsonAsync(store.GetInstanceDetail(organizationId, environmentId, instanceKey), ct);
    }
}
