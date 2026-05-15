using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNervIipCaching(builder.Configuration, "apphub");
builder.Services.AddNervIipObservability(builder.Configuration, "apphub");
builder.Services.AddSingleton<InMemoryAppHubStateStore>();

var app = builder.Build();
app.UseNervIipCorrelation();

app.MapGet("/health", () => "Healthy");
app.MapGet("/internal/apphub/v1/build-info", () => new { service = "AppHub", slice = "first-vertical-slice" });

app.MapPost("/api/connectors/v1/registrations", (ApplicationRegistration request, InMemoryAppHubStateStore store, HttpContext context) =>
{
    if (!ConnectorHostAuthorized(context, request.Context.ConnectorHostId))
    {
        return Results.Problem("Invalid Connector Host credential.", statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(store.Register(request));
});

app.MapPost("/api/connectors/v1/heartbeats", (ApplicationHeartbeat request, InMemoryAppHubStateStore store, HttpContext context) =>
{
    if (!ConnectorHostAuthorized(context, request.Context.ConnectorHostId))
    {
        return Results.Problem("Invalid Connector Host credential.", statusCode: StatusCodes.Status401Unauthorized);
    }

    store.RecordHeartbeat(request);
    return Results.NoContent();
});

app.MapPost("/api/connectors/v1/state-snapshots", (InstanceStateSnapshot request, InMemoryAppHubStateStore store, HttpContext context) =>
{
    if (!ConnectorHostAuthorized(context, request.Context.ConnectorHostId))
    {
        return Results.Problem("Invalid Connector Host credential.", statusCode: StatusCodes.Status401Unauthorized);
    }

    store.RecordStateSnapshot(request);
    return Results.NoContent();
});

app.MapPost("/internal/apphub/v1/instances/query", (InstanceListQuery query, InMemoryAppHubStateStore store) => store.QueryInstances(query));
app.MapGet("/internal/apphub/v1/instances/{instanceKey}", (string instanceKey, string organizationId, string environmentId, InMemoryAppHubStateStore store) => store.GetInstanceDetail(organizationId, environmentId, instanceKey));

app.Run();

static bool ConnectorHostAuthorized(HttpContext context, string connectorHostId)
{
    return context.Request.Headers.TryGetValue("X-Connector-Host-Id", out var hostId)
        && context.Request.Headers.TryGetValue("X-Connector-Secret", out var secret)
        && hostId == connectorHostId
        && secret == "local-connector-secret";
}

public partial class Program;
