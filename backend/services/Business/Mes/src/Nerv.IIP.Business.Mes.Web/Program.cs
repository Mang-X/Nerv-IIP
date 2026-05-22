using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.ServiceAuth;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Business MES";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
// MES MVP has no DbContext/UoW yet, so commands use MediatR IRequest until persistence lands.
builder.Services.AddSingleton<IMesPlanningStore, InMemoryMesPlanningStore>();
builder.Services.AddSingleton<RuleScheduler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new MesRescheduleOptions
{
    AutoRescheduleOnAssetUnavailable = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetUnavailable", true),
    AutoRescheduleOnAssetRestored = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetRestored", true),
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = ctx =>
        MesEndpointContracts.TryGet(ctx.EndpointType, out var contract)
            ? contract.OperationId
            : ToLowerCamelEndpointName(ctx.EndpointType.Name);
}).UseSwaggerGen();
app.Run();

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

/// <summary>
/// MES web application entry point.
/// </summary>
public partial class Program;
