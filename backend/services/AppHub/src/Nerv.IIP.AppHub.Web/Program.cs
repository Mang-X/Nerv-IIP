using DotNetCore.CAP;
using FastEndpoints;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.AppHub.Web.Application.Connectors;
using Nerv.IIP.AppHub.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.AppHub.Web.Application.IntegrationEvents;
using Nerv.IIP.Caching;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using NetCorePal.Extensions.DependencyInjection;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
var usePostgreSql = string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
if (usePostgreSql && autoMigrate && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for AppHub in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

builder.Services.AddFastEndpoints();
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IConnectorIngestionTokenService, ConnectorIngestionTokenService>();
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
builder.Services.AddAppHubPersistence(builder.Configuration);
builder.Services.AddNervIipLocalization();
builder.Services.AddAppHubIntegrationEventDeadLetterStore(usePostgreSql);
builder.Services.AddScoped<OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState>();
builder.Services.AddScoped<OperationTaskFailedIntegrationEventHandlerForRefreshInstanceState>();
builder.Services.AddScoped<AppHubPublishedEventSink>();
if (usePostgreSql)
{
    builder.Services.AddScoped<AppHubDatabaseMigrationRunner>();
}

var app = builder.Build();
if (usePostgreSql && autoMigrate)
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
