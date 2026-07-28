using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Approval.Web.Endpoints.Approvals;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Scheduling;
using Nerv.IIP.Business.Approval.Web.Application.Seed;
using Nerv.IIP.Localization;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Context.CAP;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Newtonsoft.Json;
using Prometheus;


var isTesting = false;
try
{
    var builder = WebApplication.CreateBuilder(args);
    isTesting = builder.Environment.IsEnvironment("Testing");
    builder.Services.AddNervIipObservability(builder.Configuration, "business-approval");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });
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
                s.Title = "Nerv IIP Business Approval";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.AddNetCorePalJsonConverters());
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_approval_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddApprovalPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IApprovalClock, SystemApprovalClock>();
    builder.Services.AddHostedService<ApprovalOverdueScheduler>();
    builder.Services.AddScoped<WorldHistoryApprovalSeedService>();
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddHttpContextAccessor();
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
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = ApprovalFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessApproval in Development. Use an explicit migrator, release script or migration bundle outside Development.");
    }

    if (autoMigrate)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // 《工厂世界观设定集》L1 背景历史（审批域侧）。校验器 fail-closed：对账不平就让启动失败。
    var worldHistoryEnabled = WorldHistoryConfiguration.IsEnabled(builder.Configuration);
    if (worldHistoryEnabled && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            $"'{WorldHistoryConfiguration.EnabledKey}'=true is only allowed for BusinessApproval in Development.");
    }

    if (worldHistoryEnabled)
    {
        using var scope = app.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<WorldHistoryApprovalSeedService>().SeedAsync(
            builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev",
            WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
            WorldHistoryConfiguration.ResolveScale(builder.Configuration));
        app.Logger.LogInformation(
            "World-history approval seed completed: {Templates} templates, {Chains} approval chains " +
            "({Purchase} purchase-order / {Ncr} ncr-disposition), {Pending} pending todos, {Rejected} rejected, " +
            "{Delegations} delegations; " +
            "validator checked {Checked} chains ({CheckedPending} pending / {CheckedApproved} approved / {CheckedRejected} rejected) " +
            "and {CheckedDelegations} delegations ({CheckedActive} active).",
            report.TemplatesWritten,
            report.ChainsWritten,
            report.PurchaseChainsWritten,
            report.NcrChainsWritten,
            report.PendingChainsWritten,
            report.RejectedChainsWritten,
            report.DelegationsWritten,
            report.Validation.ChainsChecked,
            report.Validation.PendingChainsChecked,
            report.Validation.ApprovedChainsChecked,
            report.Validation.RejectedChainsChecked,
            report.Validation.DelegationsChecked,
            report.Validation.ActiveDelegationsChecked);
        foreach (var line in report.Validation.Sample)
        {
            app.Logger.LogInformation("World-history approval sample: {Chain}", line);
        }
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
        c.Endpoints.NameGenerator = ctx =>
            ApprovalEndpointContracts.TryGet(ctx.EndpointType, out var contract)
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
