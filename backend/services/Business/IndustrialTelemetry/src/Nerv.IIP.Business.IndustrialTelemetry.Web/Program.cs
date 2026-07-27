using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.IndustrialTelemetry.Domain;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Scheduling;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Errors;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Historian;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.Sdk.Ops;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Prometheus;


var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNervIipObservability(builder.Configuration, "business-industrial-telemetry");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc();
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business IndustrialTelemetry";
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
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddOptions<ConnectorTagManifestIngestionOptions>()
        .BindConfiguration(ConnectorTagManifestIngestionOptions.SectionName)
        .Validate(
            options => options.MaxFutureObservationSkew > TimeSpan.Zero
                && options.MaxFutureObservationSkew <= ConnectorTagManifestIngestionOptions.MaximumConfigurableFutureObservationSkew,
            $"MaxFutureObservationSkew must be greater than zero and no more than {ConnectorTagManifestIngestionOptions.MaximumConfigurableFutureObservationSkew}.")
        .ValidateOnStart();
    builder.Services.AddScoped<TelemetryHistorianService>();
    builder.Services.AddHostedService<AlarmEscalationScheduler>();
    builder.Services.AddHostedService<TelemetryHistorianScheduler>();
    builder.Services.AddScoped<IDeviceControlOpsClient, DeviceControlOpsClient>();
    var opsBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Ops:BaseUrl", "http://localhost:5103");
    builder.Services.AddHttpClient<IOpsClient, HttpOpsClient>((services, client) =>
    {
        client.BaseAddress = opsBaseAddress;
        var token = services.GetRequiredService<IInternalServiceTokenProvider>().BearerToken;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    });

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_industrial_telemetry_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddIndustrialTelemetryPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<LeaderDemoSeedService>();
    builder.Services.AddScoped<WorldBibleSeedService>();
    builder.Services.AddScoped<WorldHistorySeedService>();
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddContext().AddEnvContext().AddCapContextProcessor();
    builder.Services.AddNetCorePalServiceDiscoveryClient();
    if (isTesting)
    {
        builder.Services.AddSingleton<IIntegrationEventDeadLetterStore, InMemoryIntegrationEventDeadLetterStore>();
        builder.Services.AddIntegrationEvents(typeof(Program));
    }
    else
    {
        builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
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
            // Must wrap unit-of-work save so save-time ingestion unique conflicts can retry through idempotent lookups.
            .AddOpenBehavior(typeof(IndustrialTelemetryIdempotentIngestionBehavior<,>))
            .AddUnitOfWorkBehaviors());
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = IndustrialTelemetryFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessIndustrialTelemetry in Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    var leaderDemoSeedEnabled = builder.Configuration.GetValue<bool>("LeaderDemo:Seed:Enabled");
    if (leaderDemoSeedEnabled && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("LeaderDemo:Seed:Enabled=true is only allowed for BusinessIndustrialTelemetry in Development.");
    }

    if (WorldHistoryConfiguration.IsEnabled(builder.Configuration) && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("LeaderDemo:History:Enabled=true is only allowed for BusinessIndustrialTelemetry in Development.");
    }

    if (leaderDemoSeedEnabled)
    {
        using var scope = app.Services.CreateScope();
        var organizationId = builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001";
        var environmentId = builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev";
        await scope.ServiceProvider.GetRequiredService<LeaderDemoSeedService>().SeedAsync(organizationId, environmentId);
        if (builder.Configuration.GetValue("LeaderDemo:World:Enabled", false))
        {
            await scope.ServiceProvider.GetRequiredService<WorldBibleSeedService>().SeedAsync(organizationId, environmentId);
        }

        // 《工厂世界观设定集》L1 设备域历史（三期）。校验器 fail-closed：对不上账就让启动失败。
        if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
        {
            var historyStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
                organizationId,
                environmentId,
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            historyStopwatch.Stop();
            app.Logger.LogInformation(
                "World-history device seed completed in {ElapsedSeconds:F1}s: {Rules} alarm rules, {Alarms} alarm events, " +
                "{Daily} daily rollups, {Hourly} hourly rollups, {Raw} raw samples, {Summaries} summaries, " +
                "{States} device states, {OeeFacts} OEE facts; validator checked {AlarmsChecked} alarms / " +
                "{DailyChecked} daily rollups / {FaultedChecked} faulted states / {OeeChecked} OEE facts ({OpenAlarms} open-tail alarms).",
                historyStopwatch.Elapsed.TotalSeconds,
                report.AlarmRulesWritten,
                report.AlarmEventsWritten,
                report.DailyRollupsWritten,
                report.HourlyRollupsWritten,
                report.RawSamplesWritten,
                report.SummariesWritten,
                report.DeviceStateSnapshotsWritten,
                report.OeeFactsWritten,
                report.Validation.AlarmsChecked,
                report.Validation.DailyRollupsChecked,
                report.Validation.FaultedStatesChecked,
                report.Validation.OeeFactsChecked,
                report.Validation.OpenAlarms);
            foreach (var line in report.Validation.Sample)
            {
                app.Logger.LogInformation("World-history device sample: {Chain}", line);
            }
        }
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = System.Net.HttpStatusCode.BadRequest });
    app.UseMiddleware<IndustrialTelemetryLifecycleConflictMiddleware>();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Serializer.Options.Converters.Add(new EquipmentRuntimeSourceTypeJsonConverter());
        c.Endpoints.NameGenerator = ctx =>
            IndustrialTelemetryEndpointContracts.TryGet(ctx.EndpointType, out var contract)
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

static Uri ResolveServiceBaseAddress(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    string configurationKey,
    string developmentFallback)
{
    var configuredBaseUrl = configuration[configurationKey];
    if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
        return new Uri(configuredBaseUrl, UriKind.Absolute);
    }

    if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
    {
        return new Uri(developmentFallback, UriKind.Absolute);
    }

    throw new InvalidOperationException($"{configurationKey} is required outside Development.");
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
