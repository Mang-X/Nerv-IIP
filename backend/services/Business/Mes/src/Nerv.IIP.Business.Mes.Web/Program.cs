using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Messaging.CAP;
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
builder.Services.AddMediatR(configuration => configuration
    .RegisterServicesFromAssembly(typeof(Program).Assembly)
    .AddUnitOfWorkBehaviors());
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
if (!builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = "Host=localhost;Database=nerv_iip_mes_testing;Username=nerv;Password=nerv";
}

builder.Services.AddMesPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
builder.Services.AddScoped<IMesPlanningStore, PersistentMesPlanningStore>();
builder.Services.AddSingleton<RuleScheduler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
builder.Services.AddSingleton(new MesRescheduleOptions
{
    AutoRescheduleOnAssetUnavailable = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetUnavailable", true),
    AutoRescheduleOnAssetRestored = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetRestored", true),
});
builder.Services.AddScoped<AssetUnavailableIntegrationEventHandlerForReschedule>();
builder.Services.AddScoped<AssetRestoredIntegrationEventHandlerForReschedule>();

var app = builder.Build();
var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
if (autoMigrate && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessMES in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

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
