using System.Reflection;
using System.Net.Http.Headers;
using Nerv.IIP.Sdk.FileStorage;
using Nerv.IIP.Business.Quality.Web;
using System.Text.Json;
using DotNetCore.CAP;
using FastEndpoints;
using Nerv.IIP.Business.Quality.Web.Endpoints.DeadLetters;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Business.Quality.Web.Application.Commands;
using Nerv.IIP.Business.Quality.Web.Application.Commands.CorrectiveActions;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Seed;
using Nerv.IIP.Business.Quality.Web.Application.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Endpoints.QualityReasons;
using Nerv.IIP.Caching;
using Nerv.IIP.DistributedLocking;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using NetCorePal.Extensions.NewtonsoftJson;
using Newtonsoft.Json;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");
builder.Services.AddNervIipIamJwtAuthentication(builder.Configuration, builder.Environment);
try
{
    builder.Services.AddNervIipObservability(builder.Configuration, "business-quality");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    var approvalBaseAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(builder.Configuration, builder.Environment, "Approval:BaseUrl", "http://localhost:5114");
    var erpBaseAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(builder.Configuration, builder.Environment, "Erp:BaseUrl", "http://localhost:5118");
    builder.Services.AddHttpClient<IApprovalChainStatusClient, HttpApprovalChainStatusClient>(client =>
    {
        client.BaseAddress = approvalBaseAddress;
    }).UseHttpClientMetrics();
    builder.Services.AddHttpClient<IErpPurchaseReceiptFactClient, HttpErpPurchaseReceiptFactClient>(client =>
    {
        client.BaseAddress = erpBaseAddress;
    }).UseHttpClientMetrics();

    var fileStorageBaseAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(
        builder.Configuration, builder.Environment, "FileStorage:BaseUrl", "http://localhost:5104");
    builder.Services.AddHttpClient<IFileStorageClient, HttpFileStorageClient>((services, client) =>
    {
        client.BaseAddress = fileStorageBaseAddress;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", services.GetRequiredService<IInternalServiceTokenProvider>().BearerToken);
    }).UseHttpClientMetrics();

    if (isTesting)
    {
        builder.Services.AddDataProtection();
    }
    else
    {
        var redis = await NervIipRedisConnection.ConnectAsync(builder.Configuration.GetConnectionString("Redis")!);
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => redis);
        builder.Services.AddDataProtection().PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");
    }

    builder.Services.AddNervIipInternalServiceAuthorization(builder.Configuration, builder.Environment);

    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business Quality";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.AddNetCorePalJsonConverters());
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();

    var qualityConnectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(qualityConnectionString))
    {
        qualityConnectionString = "Host=localhost;Database=nerv_iip_quality_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddQualityPostgreSqlPersistence(qualityConnectionString, builder.Environment.IsDevelopment());
    builder.Services.AddNervIipCommandLocking(
        builder.Configuration,
        builder.Environment,
        isTesting,
        QualityFacts.ServiceName);
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
    builder.Services.AddScoped<IntegrationEventCapFailureDeadLetterer>();
    builder.Services.AddIntegrationEventDeadLetterEndpoints();
    builder.Services.AddQualityDeadLetterReplay();
    builder.Services.AddScoped<IReworkWorkOrderBindingWriter, PostgresReworkWorkOrderBindingWriter>();
    builder.Services.AddScoped<ReworkWorkOrderBindingStore>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<QualityCodingService>();
    builder.Services.AddScoped<QualitySeedService>();
    builder.Services.AddSingleton<IInspectionUomConversionClient>(NullInspectionUomConversionClient.Instance);
    builder.Services.AddScoped<IInspectionSourceDocumentVerifier, ErpPurchaseReceiptInspectionSourceDocumentVerifier>();
    builder.Services.AddScoped<IQualityIntegrationEventContextAccessor, HttpQualityIntegrationEventContextAccessor>();
    builder.Services.AddScoped<INonconformanceReportCodeGenerator, NonconformanceReportCodeGenerator>();
    builder.Services.AddOptions<CapaAutomationOptions>()
        .Bind(builder.Configuration.GetSection("Quality:CapaAutomation"))
        .ValidateOnStart();
    builder.Services.Configure<CapaCloseApprovalOptions>(builder.Configuration.GetSection("Quality:CapaCloseApproval"));
    builder.Services.AddSingleton<IValidateOptions<CapaAutomationOptions>, CapaAutomationOptionsValidator>();
    builder.Services.AddScoped<ICorrectiveActionCodeGenerator, CorrectiveActionCodeGenerator>();
    builder.Services.AddScoped<ICapaAutomationService, CapaAutomationService>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<CollectorRegistry>(Metrics.DefaultRegistry);
    builder.Services.AddSingleton<WorkOrderReleaseFactBacklogMetrics>();
    builder.Services.AddHostedService<InspectionTaskOverdueScheduler>();
    builder.Services.AddHostedService<PeriodicInspectionTimeTaskScheduler>();
    builder.Services.AddHostedService<PeriodicInspectionQuantityContinuationScheduler>();
    builder.Services.AddHostedService<WorkOrderReleaseFactBacklogScanner>();
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
            x.UseIntegrationEventDeadLetterOnFailedThreshold();
            x.UseEntityFramework<ApplicationDbContext>();
            x.JsonSerializerOptions.AddNetCorePalJsonConverters();
            x.UseConfiguredTransport(builder.Configuration, builder.Environment.EnvironmentName);
            x.UseDashboard();
        });
    }

    builder.Services.AddMediatR(cfg =>
        cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
            .AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>))
            .AddKnownExceptionValidationBehavior()
            .AddBehavior<CreateReinspectionUniqueConflictBehavior>()
            .AddUnitOfWorkBehaviors());
    builder.Services.AddScoped<
        ICommandLock<CreateInspectionRecordFromTaskCommand>,
        CreateInspectionRecordFromTaskCommandLock>();
    builder.Services.AddScoped<
        ICommandLock<AssignInspectionTaskCommand>,
        AssignInspectionTaskCommandLock>();
    builder.Services.AddScoped<
        ICommandLock<ClaimInspectionTaskCommand>,
        ClaimInspectionTaskCommandLock>();
    builder.Services.AddScoped<
        ICommandLock<SubmitNonconformanceReportDispositionCommand>,
        SubmitNonconformanceReportDispositionCommandLock>();
    builder.Services.AddScoped<
        IQualityPersistenceConflictClassifier,
        QualityPersistenceConflictClassifier>();

    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = QualityFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    if (!isTesting)
    {
        builder.Services.AddHangfire(x => { x.UseRedisStorage(builder.Configuration.GetConnectionString("Redis")); });
        builder.Services.AddHangfireServer();
    }

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessQuality in Development. Use an explicit migrator, release script or migration bundle outside Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // 质量基础目录 seed（原因码等）：默认开启（#3805），显式 false 可关闭；幂等补缺。
    // 目标租户读 IAM 引导种子的同一组键（#3812），不再单独配置。
    var seedEnabled = builder.Configuration.GetValue("Quality:Seed:Enabled", true);
    if (seedEnabled)
    {
        using var scope = app.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<QualitySeedService>();
        await seed.SeedAsync(
            builder.Configuration["Iam:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["Iam:Seed:EnvironmentId"] ?? "env-dev");
    }

    app.UseNervIipRequestLocalization();
    app.UseKnownExceptionHandler();
    app.UseMiddleware<QualityLifecycleConflictMiddleware>();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseFastEndpoints(c =>
    {
        c.Endpoints.NameGenerator = ctx =>
        {
            if (QualityEndpointContracts.TryGet(ctx.EndpointType, out var ncrContract))
            {
                return ncrContract.OperationId;
            }

            if (QualityInspectionEndpointContracts.TryGet(ctx.EndpointType, out var inspectionContract))
            {
                return inspectionContract.OperationId;
            }

            if (QualityReasonEndpointContracts.TryGet(ctx.EndpointType, out var reasonContract))
            {
                return reasonContract.OperationId;
            }

            return QualityDeadLetterEndpointContracts.TryGet(ctx.EndpointType, out var deadLetterContract)
                ? deadLetterContract.OperationId
                : ToLowerCamelEndpointName(ctx.EndpointType.Name);
        };
    }).UseSwaggerGen();
    app.UseHttpMetrics();
    app.MapHealthChecks("/health");
    app.MapMetrics();

    if (!isTesting)
    {
        app.UseHangfireDashboard();
    }

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
