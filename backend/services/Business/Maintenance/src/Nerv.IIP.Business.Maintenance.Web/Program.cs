using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Prometheus;


var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNervIipObservability(builder.Configuration, "business-maintenance");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc();
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business Maintenance";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o =>
    {
        o.SerializerOptions.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
        o.SerializerOptions.AddNetCorePalJsonConverters();
    });
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, MaintenanceIntegrationEventDeadLetterStore>();
    builder.Services.AddScoped<OpenWorkOrderWhenAlarmRaisedHandler>();
    builder.Services.AddScoped<MarkWorkOrderAlarmClearedHandler>();
    builder.Services.AddHostedService<MaintenancePlanDueScheduler>();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_maintenance_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddMaintenancePostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<MaintenanceCodingService>();
    builder.Services.AddContext().AddEnvContext().AddCapContextProcessor();
    builder.Services.AddNetCorePalServiceDiscoveryClient();
    if (isTesting)
    {
        builder.Services.AddIntegrationEvents(typeof(Program));
    }
    else
    {
        builder.Services.AddIntegrationEvents(typeof(Program))
            .UseCap<ApplicationDbContext>(b =>
            {
                b.RegisterServicesFromAssemblies(typeof(Program));
                b.AddContextIntegrationFilters();
            });

        builder.Services.AddCap(x =>
        {
            x.Version = builder.Configuration["Cap:Version"] ?? "v1";
            x.UseEntityFramework<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
            x.UseDashboard();
        });
    }

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddCommandLockBehavior()
            .AddKnownExceptionValidationBehavior()
            .AddUnitOfWorkBehaviors());
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = MaintenanceFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessMaintenance in Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Serializer.Options.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
        c.Endpoints.NameGenerator = ctx =>
            MaintenanceEndpointContracts.TryGet(ctx.EndpointType, out var contract)
                ? contract.OperationId
                : ToLowerCamelEndpointName(ctx.EndpointType.Name);
    }).UseSwaggerGen();
    app.UseHttpMetrics();
    app.MapHealthChecks("/health");
    app.MapMetrics();

    await app.RunAsync();
}
catch (Exception ex)
{
    if (isTesting)
    {
        throw;
    }

    await Console.Error.WriteLineAsync($"Application terminated unexpectedly: {ex}");
}

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
