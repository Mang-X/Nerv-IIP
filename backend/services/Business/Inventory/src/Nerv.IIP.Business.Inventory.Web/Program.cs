using System.Reflection;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Approval;
using Nerv.IIP.Business.Inventory.Web.Application.Expiry;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.MasterData;
using Nerv.IIP.Business.Inventory.Web.Application.Seed;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;
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
    builder.Services.AddNervIipObservability(builder.Configuration, "business-inventory");

    builder.Services.AddHealthChecks();
    builder.Services.AddMvc()
        .AddNewtonsoftJson(options => { options.SerializerSettings.AddNetCorePalJsonConverters(); });
    builder.Services.AddHealthChecks().ForwardToPrometheus();
    builder.Services.AddHttpClient(Options.DefaultName).UseHttpClientMetrics();
    var masterDataBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
    builder.Services.AddHttpClient<IInventorySkuExpiryPolicyProvider, HttpInventorySkuExpiryPolicyProvider>(client =>
    {
        client.BaseAddress = masterDataBaseAddress;
        client.Timeout = TimeSpan.FromSeconds(2);
    }).UseHttpClientMetrics();
    builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
    builder.Services.AddControllers().AddNetCorePalSystemTextJson();
    builder.Services
        .AddFastEndpoints(o => o.IncludeAbstractValidators = true)
        .SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "Nerv IIP Business Inventory";
                s.Version = "v1";
            };
        });
    builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.AddNetCorePalJsonConverters());
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
    builder.Services.AddKnownExceptionErrorModelInterceptor();
    builder.Services.AddNervIipLocalization();
    builder.Services.Configure<ExpiredStockBlockingOptions>(builder.Configuration.GetSection("Inventory:ExpiredStockBlocking"));
    builder.Services.Configure<StockReservationExpirationOptions>(builder.Configuration.GetSection("Inventory:ReservationExpiration"));
    builder.Services.Configure<StockCountAdjustmentApprovalOptions>(builder.Configuration.GetSection(StockCountAdjustmentApprovalOptions.SectionName));
    builder.Services.Configure<InventoryForwardedPermissionOptions>(builder.Configuration.GetSection("Inventory:ForwardedPermissions"));
    builder.Services.AddScoped<ExpiredStockBlockingService>();
    builder.Services.AddScoped<ExpiredStockReservationService>();
    builder.Services.AddSingleton<InventoryReservationMetrics>();
    builder.Services.AddHostedService<ExpiredStockBlockingHostedService>();
    builder.Services.AddHostedService<ExpiredStockReservationHostedService>();
    var approvalBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Approval:BaseUrl", "http://localhost:5114");
    builder.Services.AddHttpClient<IStockCountApprovalClient, HttpStockCountApprovalClient>(client =>
    {
        client.BaseAddress = approvalBaseAddress;
    }).UseHttpClientMetrics();

    var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
    if (isTesting && string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Host=localhost;Database=nerv_iip_inventory_testing;Username=nerv;Password=nerv";
    }

    builder.Services.AddInventoryPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
    builder.Services.AddScoped<LeaderDemoSeedService>();
    builder.Services.AddInMemoryDistributedLock();
    builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
    builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IInventoryIntegrationEventContextAccessor, HttpInventoryIntegrationEventContextAccessor>();
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
            .AddOpenBehavior(typeof(CreateStockCountTaskUniqueConflictBehavior<,>))
            .AddUnitOfWorkBehaviors());
    builder.Services.AddScoped<ICommandLock<CreateStockCountTaskCommand>, CreateStockCountTaskCommandLock>();
    builder.Services.AddMultiEnv(envOption => envOption.ServiceName = InventoryFacts.ServiceName)
        .UseMicrosoftServiceDiscovery();
    builder.Services.AddConfigurationServiceEndpointProvider();

    var app = builder.Build();
    app.UseNervIipCorrelation();
    var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
    if (autoMigrate && !app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessInventory in Development. Use an explicit migrator, release script or migration bundle outside Development.");
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
        throw new InvalidOperationException("LeaderDemo:Seed:Enabled=true is only allowed for BusinessInventory in Development.");
    }

    if (leaderDemoSeedEnabled)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LeaderDemoSeedService>().SeedAsync(
            builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001",
            builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev");
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
            InventoryEndpointContracts.TryGet(ctx.EndpointType, out var contract)
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

static Uri ResolveServiceBaseAddress(IConfiguration configuration, IHostEnvironment environment, string key, string developmentDefault)
{
    var configured = configuration[key];
    if (string.IsNullOrWhiteSpace(configured))
    {
        configured = environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? developmentDefault
            : throw new InvalidOperationException($"{key} is required outside Development.");
    }

    return new Uri(configured, UriKind.Absolute);
}

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
