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
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;
using Nerv.IIP.Business.Maintenance.Web.Endpoints.Maintenance;
using Nerv.IIP.Business.Maintenance.Web.Infrastructure;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Prometheus;
using StackExchange.Redis;


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
    var industrialTelemetryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, "IndustrialTelemetry:BaseUrl", "http://localhost:5116");
    builder.Services.AddHttpClient(HttpIndustrialTelemetryAssetRuntimeHoursProvider.ClientName, client =>
    {
        client.BaseAddress = industrialTelemetryBaseAddress;
    }).UseHttpClientMetrics();
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
    builder.Services.Configure<MaintenanceCompletionOptions>(builder.Configuration.GetSection("Maintenance:Completion"));
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, MaintenanceIntegrationEventDeadLetterStore>();
    builder.Services.AddScoped<OpenWorkOrderWhenAlarmRaisedHandler>();
    builder.Services.AddScoped<MarkWorkOrderAlarmClearedHandler>();
    builder.Services.AddScoped<PauseMaintenancePlansWhenDeviceDisabledHandler>();
    builder.Services.AddScoped<ICommandLock<GenerateDueMaintenanceWorkOrdersCommand>, GenerateDueMaintenanceWorkOrdersCommandLock>();
    builder.Services.AddScoped<ICommandLock<ApplyMaintenanceDeviceStateCommand>, ApplyMaintenanceDeviceStateCommandLock>();
    builder.Services.AddScoped<ICommandLock<CreateMaintenancePlanCommand>, CreateMaintenancePlanCommandLock>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddHostedService<MaintenancePlanDueScheduler>();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_maintenance_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddMaintenancePostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<MaintenanceUnavailableWindowRuntimeHoursProvider>();
    builder.Services.AddScoped<IAssetRuntimeHoursFallbackProvider>(sp => sp.GetRequiredService<MaintenanceUnavailableWindowRuntimeHoursProvider>());
    if (isTesting)
    {
        builder.Services.AddScoped<IAssetRuntimeHoursProvider, MaintenanceUnavailableWindowRuntimeHoursProvider>();
    }
    else
    {
        builder.Services.AddScoped<IAssetRuntimeHoursProvider, HttpIndustrialTelemetryAssetRuntimeHoursProvider>();
    }

    AddMaintenanceDistributedLock(builder.Services, builder.Configuration, builder.Environment, isTesting);
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<MaintenanceCodingService>();
    builder.Services.AddScoped<MaintenanceSeedService>();
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
            .AddOpenBehavior(typeof(MaintenanceCommandLockBehavior<,>))
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

    // 点检保养计划 seed（默认随 autoMigrate 开启，或显式 Maintenance:Seed:Enabled）：
    // 全新环境补齐可选保养计划，供 PDA 点检页选计划 → 录测量值/超差/拍照走通（幂等只补缺失）。
    var seedEnabled = builder.Configuration.GetValue<bool>("Maintenance:Seed:Enabled") || autoMigrate;
    if (seedEnabled)
    {
        using var scope = app.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<MaintenanceSeedService>();
        await seed.SeedAsync(
            builder.Configuration["Maintenance:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["Maintenance:Seed:EnvironmentId"] ?? "env-dev");
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

static Uri ResolveServiceBaseAddress(IConfiguration configuration, string configurationKey, string fallback)
{
    var value = configuration[configurationKey];
    return Uri.TryCreate(value, UriKind.Absolute, out var configured)
        ? configured
        : new Uri(fallback, UriKind.Absolute);
}

static void AddMaintenanceDistributedLock(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, bool isTesting)
{
    if (isTesting)
    {
        services.AddInMemoryDistributedLock();
        return;
    }

    var redisConnectionString = ResolveRedisConnectionString(configuration);
    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        if (environment.IsDevelopment())
        {
            services.AddInMemoryDistributedLock();
            return;
        }

        throw new InvalidOperationException("BusinessMaintenance distributed command locks require a Redis connection string outside Development. Set ConnectionStrings:Redis, Messaging:Redis:ConnectionString, or Caching:Redis.");
    }

    services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });
    services.AddSingleton<IRedisCommandLockStore>(sp => new StackExchangeRedisCommandLockStore(sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase()));
    services.AddSingleton<IDistributedLock>(sp => new RedisMaintenanceDistributedLock(
        sp.GetRequiredService<IRedisCommandLockStore>(),
        sp.GetRequiredService<TimeProvider>(),
        logger: sp.GetRequiredService<ILogger<RedisMaintenanceDistributedLock>>()));
}

static string? ResolveRedisConnectionString(IConfiguration configuration)
{
    return configuration.GetConnectionString("Redis")
        ?? configuration["Messaging:Redis:ConnectionString"]
        ?? configuration["ConnectionStrings:Redis"]
        ?? configuration["Caching:Redis"];
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
