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
    var masterDataBaseAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
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
    var approvalBaseAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(builder.Configuration, builder.Environment, "Approval:BaseUrl", "http://localhost:5114");
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
    builder.Services.AddScoped<WorldHistorySeedService>();
    builder.Services.AddScoped<WorldHistoryCountSeedService>();
    builder.Services.AddScoped<WorldHistoryReservationSeedService>();
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
        var leaderDemoOrganizationId = builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001";
        var leaderDemoEnvironmentId = builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev";
        await scope.ServiceProvider.GetRequiredService<LeaderDemoSeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId);

        // 《工厂世界观设定集》L1 背景历史（库存域侧）。校验器 fail-closed：对账不平就让启动失败。
        if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
        {
            var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
                leaderDemoOrganizationId,
                leaderDemoEnvironmentId,
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            app.Logger.LogInformation(
                "World-history inventory seed completed: {Locations} stock locations, {Movements} stock movements, " +
                "{Ledgers} new ledger dimensions; validator checked {CheckedMovements} movements / {CheckedLedgers} ledgers " +
                "across {Lots} lots: opening {Opening}, inbound {Inbound}, outbound {Outbound}, closing {Closing}.",
                report.StockLocationsWritten,
                report.StockMovementsWritten,
                report.StockLedgersCreated,
                report.Validation.StockMovementsChecked,
                report.Validation.StockLedgersChecked,
                report.Validation.DistinctLotsChecked,
                report.Validation.OpeningQuantityTotal,
                report.Validation.InboundQuantityTotal,
                report.Validation.OutboundQuantityTotal,
                report.Validation.ClosingQuantityTotal);
            foreach (var line in report.Validation.Sample)
            {
                app.Logger.LogInformation("World-history sample: {Movement}", line);
            }

            // 预留块排在流水块之后：预留的维度取自真实落库的台账。
            // 它只动 ReservedQuantity / LedgerVersion，绝不改现存量、绝不写流水（校验器 fail-closed）。
            //
            // #1374 · **预留必须排在盘点之前**。`StockLedger.Reserve` 会 `LedgerVersion++`，
            // 而盘点任务把下发时的 `LedgerVersion` 存成 `ExpectedLedgerVersion`，确认差异时逐字比对
            // （`StockCountTask.ConfirmAdjustment`）。两块的维度 100% 重叠（都是 22 条期初批），
            // 先盘点后预留会把每一张盘点任务的快照版本当场捅穿——62 张任务一出生即死单。
            // 顺序不是风格问题：盘点块的校验器现在硬断言两者相等（见 WorldHistoryCountValidator）。
            var reservationReport = await scope.ServiceProvider.GetRequiredService<WorldHistoryReservationSeedService>().SeedAsync(
                leaderDemoOrganizationId,
                leaderDemoEnvironmentId,
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            app.Logger.LogInformation(
                "World-history inventory reservation seed completed: {Reservations} reservations ({Open} still committing stock), " +
                "{Skipped} plans skipped for a missing ledger dimension, {NotKitted} skipped as not kitted; " +
                "validator checked {CheckedReservations} reservations " +
                "({CheckedOpen} open) across {LedgersWithReservation} committed ledgers: reserved {Reserved}, available {Available}.",
                reservationReport.StockReservationsWritten,
                reservationReport.OpenReservationsWritten,
                reservationReport.PlansSkippedWithoutLedger,
                reservationReport.PlansSkippedNotKitted,
                reservationReport.Validation.StockReservationsChecked,
                reservationReport.Validation.OpenReservationsChecked,
                reservationReport.Validation.LedgersWithReservationChecked,
                reservationReport.Validation.ReservedQuantityTotal,
                reservationReport.Validation.AvailableQuantityTotal);
            foreach (var line in reservationReport.Validation.Sample)
            {
                app.Logger.LogInformation("World-history reservation sample: {Reservation}", line);
            }

            // 盘点块必须排在流水块与预留块之后：盘点任务的维度与期望台账版本都取自真实落库的台账，
            // 而快照必须是**本次 seed 落幕时**的版本，否则任务一出生就过期（见上方 #1374 注释）。
            var countReport = await scope.ServiceProvider.GetRequiredService<WorldHistoryCountSeedService>().SeedAsync(
                leaderDemoOrganizationId,
                leaderDemoEnvironmentId,
                WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
                WorldHistoryConfiguration.ResolveScale(builder.Configuration));
            app.Logger.LogInformation(
                "World-history inventory count seed completed: {Tasks} count tasks, {Adjustments} count adjustments, " +
                "{Skipped} plans skipped for a missing ledger dimension; validator checked {CheckedTasks} tasks " +
                "({PendingApproval} pending approval, {Recount} recount required, {Cancelled} cancelled, {Open} open) " +
                "and {CheckedAdjustments} adjustments totalling {VarianceAmount} in variance value.",
                countReport.StockCountTasksWritten,
                countReport.StockCountAdjustmentsWritten,
                countReport.PlansSkippedWithoutLedger,
                countReport.Validation.StockCountTasksChecked,
                countReport.Validation.PendingApprovalTasksChecked,
                countReport.Validation.RecountRequiredTasksChecked,
                countReport.Validation.CancelledTasksChecked,
                countReport.Validation.OpenTasksChecked,
                countReport.Validation.StockCountAdjustmentsChecked,
                countReport.Validation.VarianceAmountTotal);
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

#pragma warning disable S1118
public partial class Program
#pragma warning restore S1118
{
}
