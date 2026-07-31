using System.Reflection;
using System.Text.Json;
using DotNetCore.CAP;
using FastEndpoints;
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

var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
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

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters.ValidAudience = "netcorepal";
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidIssuer = "netcorepal";
            options.TokenValidationParameters.ValidateIssuer = true;
        });
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
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<QualityCodingService>();
    builder.Services.AddScoped<QualitySeedService>();
    builder.Services.AddScoped<LeaderDemoSeedService>();
    builder.Services.AddScoped<WorldHistorySeedService>();
    builder.Services.AddScoped<WorldHistoryMetrologySeedService>();
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
    builder.Services.AddHostedService<InspectionTaskOverdueScheduler>();
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

    // 质量基础目录 seed（原因码等）：与 MasterData 同口径——显式开关或本地 autoMigrate 时执行，幂等。
    var seedEnabled = builder.Configuration.GetValue<bool>("Quality:Seed:Enabled") || autoMigrate;
    if (seedEnabled)
    {
        using var scope = app.Services.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<QualitySeedService>();
        await seed.SeedAsync(
            builder.Configuration["Quality:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["Quality:Seed:EnvironmentId"] ?? "env-dev");
    }

    var leaderDemoSeedEnabled = builder.Configuration.GetValue<bool>("LeaderDemo:Seed:Enabled");
    if (leaderDemoSeedEnabled && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("LeaderDemo:Seed:Enabled=true is only allowed for BusinessQuality in Development.");
    }

    if (leaderDemoSeedEnabled)
    {
        using var scope = app.Services.CreateScope();
        var leaderDemoOrganizationId = builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001";
        var leaderDemoEnvironmentId = builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev";
        await scope.ServiceProvider.GetRequiredService<LeaderDemoSeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId);

        // 《工厂世界观设定集》L1 背景历史（质量域侧）。校验器 fail-closed：对账不平就让启动失败。
        if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
        {
            var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
                leaderDemoOrganizationId,
                leaderDemoEnvironmentId,
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            app.Logger.LogInformation(
                "World-history quality seed completed: {Plans} inspection plans, {Tasks} inspection tasks, " +
                "{Records} inspection records ({Reinspections} reinspections), {Ncrs} nonconformance reports; " +
                "validator checked {CheckedTasks} tasks / {CheckedCompleted} completed inspections / " +
                "{CheckedRecords} records / {CheckedNcrs} NCRs at a {Rate:P2} nonconforming rate.",
                report.InspectionPlansWritten,
                report.InspectionTasksWritten,
                report.InspectionRecordsWritten,
                report.ReinspectionRecordsWritten,
                report.NonconformanceReportsWritten,
                report.Validation.InspectionTasksChecked,
                report.Validation.CompletedInspectionsChecked,
                report.Validation.InspectionRecordsChecked,
                report.Validation.NonconformanceReportsChecked,
                report.Validation.NonconformingRate);
            foreach (var line in report.Validation.Sample)
            {
                app.Logger.LogInformation("World-history sample: {Chain}", line);
            }

            // 三期：计量器具台账 / 校准记录 / SPC 控制限 / CAPA。必须排在二期之后——
            // CAPA 要挂真实 NCR、效果验证要引用真实合格检验记录，两者都由二期写入。
            var metrologyReport = await scope.ServiceProvider
                .GetRequiredService<WorldHistoryMetrologySeedService>()
                .SeedAsync(
                    leaderDemoOrganizationId,
                    leaderDemoEnvironmentId,
                    WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                    WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            app.Logger.LogInformation(
                "World-history metrology seed completed: {Devices} measuring devices, {Calibrations} calibration records, " +
                "{Charts} SPC control charts, {Capas} CAPAs ({Items} action items); validator checked " +
                "{CheckedDevices} devices (overdue {Overdue} / warning {Warning} / unavailable {Unavailable}), " +
                "{CheckedCharts} charts, {CheckedCapas} CAPAs ({ClosedCapas} closed, {OverdueCapas} overdue).",
                metrologyReport.MeasuringDevicesWritten,
                metrologyReport.CalibrationRecordsWritten,
                metrologyReport.SpcControlChartsWritten,
                metrologyReport.CorrectiveActionsWritten,
                metrologyReport.CorrectiveActionItemsWritten,
                metrologyReport.Validation.MeasuringDevicesChecked,
                metrologyReport.Validation.OverdueDevices,
                metrologyReport.Validation.WarningDevices,
                metrologyReport.Validation.UnavailableDevices,
                metrologyReport.Validation.SpcControlChartsChecked,
                metrologyReport.Validation.CorrectiveActionsChecked,
                metrologyReport.Validation.ClosedCorrectiveActions,
                metrologyReport.Validation.OverdueCorrectiveActions);
            foreach (var line in metrologyReport.Validation.Sample)
            {
                app.Logger.LogInformation("World-history metrology sample: {Chain}", line);
            }
        }
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

            return QualityReasonEndpointContracts.TryGet(ctx.EndpointType, out var reasonContract)
                ? reasonContract.OperationId
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
