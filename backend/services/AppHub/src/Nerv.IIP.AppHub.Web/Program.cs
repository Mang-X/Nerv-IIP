using DotNetCore.CAP;
using FastEndpoints;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using Nerv.IIP.AppHub.Web.Application.Commands;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.Caching;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.Persistence;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using NetCorePal.Extensions.DependencyInjection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var persistence = PersistenceStartupGovernance.Resolve(
    builder.Configuration,
    builder.Environment,
    new PersistenceStartupRequirements("AppHub", ["AppHubDb", "PostgreSQL"]));
var usePostgreSql = persistence.UsePostgreSql;

builder.Services.AddFastEndpoints();
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IConnectorIngestionTokenService, ConnectorIngestionTokenService>();
builder.Services
    .AddOptions<ConnectorCollectionHealthOptions>()
    .Bind(builder.Configuration.GetSection(ConnectorCollectionHealthOptions.SectionName))
    .Validate(
        options => options.HostHeartbeatCadence > TimeSpan.Zero,
        "CollectionHealth:HostHeartbeatCadence must be positive.")
    .Validate(
        options => options.BackendDeadline > TimeSpan.Zero
            && options.BackendDeadline <= ConnectorCollectionHealthOptions.MaximumBackendDeadline,
        "CollectionHealth:BackendDeadline must be positive and no greater than 00:00:08.")
    .Validate(
        options => options.HasValidHostLivenessWindow(),
        "CollectionHealth:HostLivenessTimeout must be at least three heartbeat cadences and no greater than the backend deadline or 00:00:08.")
    .ValidateOnStart();
builder.Services.AddSingleton<ConnectorCollectionHealthEvaluator>();
builder.Services.AddMediatR(configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
    configuration.AddKnownExceptionValidationBehavior();
    if (usePostgreSql)
    {
        configuration.AddUnitOfWorkBehaviors();
    }
});
builder.Services.AddNervIipCaching(builder.Configuration, "apphub");
builder.Services.AddNervIipObservability(builder.Configuration, "apphub");
if (usePostgreSql)
{
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddContext();
    builder.Services.AddEnvContext("X-Environment-Id");
    builder.Services.AddCapContextProcessor();
    builder.Services.AddIntegrationEvents(typeof(Program))
        .UseCap<ApplicationDbContext>(b =>
        {
            b.RegisterServicesFromAssemblies(typeof(Program));
            b.AddContextIntegrationFilters();
        });
    builder.Services.AddCap(options =>
    {
        options.Version = builder.Configuration["Cap:Version"] ?? "v1";
        options.UseEntityFramework<ApplicationDbContext>();
        options.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
    });
}
else
{
    builder.Services.AddIntegrationEvents(typeof(Program));
    builder.Services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
}
builder.Services.AddAppHubPersistence(builder.Configuration, usePostgreSql);
builder.Services.Configure<AppHubHeartbeatTimeoutScanOptions>(
    builder.Configuration.GetSection(AppHubHeartbeatTimeoutScanOptions.SectionName));
builder.Services.AddNervIipLocalization();
builder.Services.AddAppHubIntegrationEventDeadLetterStore(usePostgreSql);
builder.Services.AddScoped<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>();
builder.Services.AddScoped<OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState>();
builder.Services.AddScoped<AppHubPublishedEventSink>();
if (usePostgreSql)
{
    builder.Services.AddScoped<AppHubDatabaseMigrationRunner>();
    builder.Services.AddScoped<AppHubHeartbeatTimeoutScanner>();
    builder.Services.AddHostedService<AppHubHeartbeatTimeoutScanWorker>();
}

var app = builder.Build();
if (usePostgreSql && persistence.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppHubDatabaseMigrationRunner>().MigrateAsync();
}

app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
if (usePostgreSql)
{
    app.UseContext();
}
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();
app.Run();

public partial class Program;
