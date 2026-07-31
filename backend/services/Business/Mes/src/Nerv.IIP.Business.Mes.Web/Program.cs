using FastEndpoints;
using FastEndpoints.Swagger;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Seed;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Business.Mes.Web;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.DistributedLocking;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using NetCorePal.Extensions.DistributedTransactions.CAP;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");
builder.Services.AddNervIipObservability(builder.Configuration, "business-mes");

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Business MES";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IMesIntegrationEventContextAccessor, HttpMesIntegrationEventContextAccessor>();
var productEngineeringBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "ProductEngineering:BaseUrl", "http://localhost:5108");
var inventoryBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "Inventory:BaseUrl", "http://localhost:5109");
var masterDataBaseAddress = InternalServiceBaseAddress.Resolve(builder.Configuration, builder.Environment, "MasterData:BaseUrl", "http://localhost:5107");
// `Inventory:SiteCode` 是唯一权威的站点键。`Inventory:SiteCodes`（复数）保留给真正的多站点部署
// —— 齐套可用量需要跨站点求和，与「本服务归属哪个站点」不是同一件事，因此不能合并；
// 未显式配置时它回落到权威键，不再各自留一份默认值。
var inventorySiteCode = builder.Configuration["Inventory:SiteCode"] ?? string.Empty;
builder.Services.AddSingleton(new MesMaterialRequirementInventoryOptions
{
    DefaultSiteCode = inventorySiteCode,
    SiteCodes = ResolveSiteCodes(builder.Configuration) ?? (string.IsNullOrWhiteSpace(inventorySiteCode) ? null : [inventorySiteCode]),
});
// 完工入库目标位置：配置驱动（#1331），缺失时由 resolver 显式 KnownException，绝不回落到硬编码命名空间。
// 站点复用上面的权威键 `Inventory:SiteCode`；只有成品仓独立成站点的部署才需要 `Inventory:FinishedGoodsSiteCode`。
var finishedGoodsSiteCode = builder.Configuration["Inventory:FinishedGoodsSiteCode"];
builder.Services.AddSingleton(new MesFinishedGoodsReceiptLocationOptions
{
    SiteCode = string.IsNullOrWhiteSpace(finishedGoodsSiteCode) ? inventorySiteCode : finishedGoodsSiteCode,
    LocationCode = builder.Configuration["Inventory:FinishedGoodsLocationCode"] ?? string.Empty,
});
builder.Services.AddSingleton<IMesFinishedGoodsReceiptLocationResolver, ConfiguredMesFinishedGoodsReceiptLocationResolver>();
builder.Services.AddHttpClient<MesProductEngineeringHttpClient>(client =>
{
    client.BaseAddress = productEngineeringBaseAddress;
});
builder.Services.AddHttpClient<MesInventoryHttpClient>(client =>
{
    client.BaseAddress = inventoryBaseAddress;
});
builder.Services.AddHttpClient<MesMasterDataHttpClient>(client =>
{
    client.BaseAddress = masterDataBaseAddress;
});
builder.Services.Configure<MesMaterialSupplyLocationOptions>(builder.Configuration.GetSection("Inventory"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MesMaterialSupplyLocationOptions>>().Value);
builder.Services.AddScoped<IMesMaterialSupplyLocationResolver, InventoryMesMaterialSupplyLocationResolver>();
builder.Services.AddScoped<IMesMaterialRequirementSnapshotProvider, HttpMesProductEngineeringMaterialRequirementSnapshotProvider>();
builder.Services.AddScoped<IMesRoutingSnapshotProvider, HttpMesProductEngineeringRoutingSnapshotProvider>();
builder.Services.AddScoped<LeaderDemoSeedService>();
builder.Services.AddScoped<LeaderDemoScaleSeedService>();
builder.Services.AddScoped<IWorldHistoryProductionVersionResolver, WorldHistoryProductionVersionResolver>();
builder.Services.AddScoped<WorldHistorySeedService>();
builder.Services.AddScoped<WorldHistoryFloorEventsSeedService>();
builder.Services.AddScoped<WorldHistoryGenealogySeedService>();
builder.Services.AddScoped<WorldHistoryFoundationSeedService>();
builder.Services.AddScoped<WorldHistoryScheduleResultSeedService>();
// Register the FluentValidation command validators (CancelWorkOrder/ReturnLineSideMaterial/... — 11 in total)
// so the MediatR AddKnownExceptionValidationBehavior below can execute them. Without both lines the validators
// are dead code and command-level validation never runs — matching every other business service.
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddNervIipCommandLocking(
    builder.Configuration,
    builder.Environment,
    isTesting,
    "business-mes");
builder.Services.AddMediatR(configuration => configuration
    .RegisterServicesFromAssembly(typeof(Program).Assembly)
    .AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>))
    .AddKnownExceptionValidationBehavior()
    .AddOpenBehavior(typeof(ManualDispatchConcurrencyRetryBehavior<,>))
    .AddUnitOfWorkBehaviors());
builder.Services.AddScoped<
    NetCorePal.Extensions.Primitives.ICommandLock<ChangeOperationTaskStateCommand>,
    ChangeOperationTaskStateCommandLock>();
// Surface KnownException (business-rule violations, e.g. cancelling a work order whose received
// material has no returnable lot) as the standard success=false envelope instead of an unhandled
// HTTP 500 — matching every other business service. Without it the gateway sees a 500 and returns
// a generic "downstream-request-failed", hiding the business message from the user.
builder.Services.AddKnownExceptionErrorModelInterceptor();
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL");
if (!builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = "Host=localhost;Database=nerv_iip_mes_testing;Username=nerv;Password=nerv";
}

builder.Services.AddMesPostgreSqlPersistence(connectionString, builder.Environment.IsDevelopment());
builder.Services.AddScoped<IMesPlanningStore, PersistentMesPlanningStore>();
builder.Services.AddScoped<MesFoundationReadinessService>();
builder.Services.Configure<MesEngineeringChangeOptions>(
    builder.Configuration.GetSection("Mes:EngineeringChange"));
builder.Services.AddSingleton<RuleScheduler>();
builder.Services.AddScoped<MesCodingService>();
builder.Services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
builder.Services.AddMesCapIntegrationEvents(builder.Configuration, builder.Environment.EnvironmentName, isTesting);
builder.Services.AddSingleton(new MesRescheduleOptions
{
    AutoRescheduleOnAssetUnavailable = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetUnavailable", true),
    AutoRescheduleOnAssetRestored = builder.Configuration.GetValue("Mes:AutoRescheduleOnAssetRestored", true),
});
builder.Services.AddMesIntegrationEventConsumers();

var app = builder.Build();
app.UseNervIipCorrelation();
var autoMigrate = builder.Configuration.GetValue<bool>("Persistence:AutoMigrate");
if (autoMigrate && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Persistence:AutoMigrate=true is only allowed for BusinessMES in Development. Use an explicit migrator, release script or migration bundle outside Development.");
}

if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseKnownExceptionHandler();
app.UseMiddleware<MesLifecycleConflictMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = ctx =>
        MesEndpointContracts.TryGet(ctx.EndpointType, out var contract)
            ? contract.OperationId
            : ToLowerCamelEndpointName(ctx.EndpointType.Name);
}).UseSwaggerGen();

var leaderDemoSeedEnabled = builder.Configuration.GetValue<bool>("LeaderDemo:Seed:Enabled");
if (leaderDemoSeedEnabled && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("LeaderDemo:Seed:Enabled=true is only allowed for BusinessMES in Development.");
}

// Resolve the released ProductEngineering version through the real HTTP boundary before MES
// accepts traffic. This keeps the normal ProductEngineering client timeout unchanged and prevents
// /health from becoming green while the bounded leader-demo prerequisite is still converging.
if (leaderDemoSeedEnabled)
{
    using var scope = app.Services.CreateScope();
    var leaderDemoOrganizationId = builder.Configuration["LeaderDemo:Seed:OrganizationId"] ?? "org-001";
    var leaderDemoEnvironmentId = builder.Configuration["LeaderDemo:Seed:EnvironmentId"] ?? "env-dev";
    await scope.ServiceProvider.GetRequiredService<LeaderDemoSeedService>().SeedAsync(
        leaderDemoOrganizationId,
        leaderDemoEnvironmentId);
    await scope.ServiceProvider.GetRequiredService<LeaderDemoScaleSeedService>().SeedAsync(
        leaderDemoOrganizationId,
        leaderDemoEnvironmentId,
        builder.Configuration.GetValue("LeaderDemo:Scale:OrderCount", 0),
        DateTimeOffset.UtcNow);

    // 《工厂世界观设定集》L1 背景历史（MES 侧）。校验器 fail-closed：数量链不平就让启动失败。
    if (WorldHistoryConfiguration.IsEnabled(builder.Configuration))
    {
        var report = await scope.ServiceProvider.GetRequiredService<WorldHistorySeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId,
            WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
            WorldHistoryConfiguration.ResolveScale(builder.Configuration));
        app.Logger.LogInformation(
            "World-history MES seed completed: {OrderWorkOrders} order work orders, {ReworkWorkOrders} rework work orders; " +
            "validator checked {WorkOrders} work orders / {Tasks} operation tasks / {Reports} production reports / " +
            "{Receipts} finished-goods receipts.",
            report.OrderWorkOrdersWritten,
            report.ReworkWorkOrdersWritten,
            report.Validation.WorkOrdersChecked,
            report.Validation.OperationTasksChecked,
            report.Validation.ProductionReportsChecked,
            report.Validation.FinishedGoodsReceiptsChecked);
        foreach (var line in report.Validation.Sample)
        {
            app.Logger.LogInformation("World-history sample: {Chain}", line);
        }

        // L1「异常与协同」块：停机事件 / 班次交接 / 车间不良。必须在工单链之后跑——
        // 不良只挂在已落库的真实工单与工序任务上。
        var floorEvents = await scope.ServiceProvider.GetRequiredService<WorldHistoryFloorEventsSeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId,
            WorldHistoryConfiguration.ResolveAsOfDate(builder.Configuration),
            WorldHistoryConfiguration.ResolveScale(builder.Configuration));
        app.Logger.LogInformation(
            "World-history MES floor-event seed completed: {Downtime} downtime events, {Handovers} shift handovers, " +
            "{Defects} defect records; validator checked {CheckedDowntime}/{CheckedHandovers}/{CheckedDefects}.",
            floorEvents.DowntimeEventsWritten,
            floorEvents.ShiftHandoversWritten,
            floorEvents.DefectRecordsWritten,
            floorEvents.Validation.DowntimeEventsChecked,
            floorEvents.Validation.ShiftHandoversChecked,
            floorEvents.Validation.DefectRecordsChecked);

        // L1「追溯断点」块：产出批次谱系 / 报工物料消耗。同样必须在工单链之后跑——
        // 两张表都有指向 production_reports / work_orders / operation_tasks 的真实外键。
        var genealogy = await scope.ServiceProvider.GetRequiredService<WorldHistoryGenealogySeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId);
        app.Logger.LogInformation(
            "World-history MES genealogy seed completed: {Genealogies} output lot genealogies, " +
            "{Consumptions} material consumptions; validator checked {CheckedGenealogies}/{CheckedConsumptions}.",
            genealogy.OutputLotGenealogiesWritten,
            genealogy.MaterialConsumptionsWritten,
            genealogy.Validation.OutputLotGenealogiesChecked,
            genealogy.Validation.MaterialConsumptionsChecked);

        // L1「生产准备底座」块：设备 ↔ 工作中心映射 / SKU 停用投影（主数据投影，与工单链无先后依赖）。
        var foundation = await scope.ServiceProvider.GetRequiredService<WorldHistoryFoundationSeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId);
        app.Logger.LogInformation(
            "World-history MES foundation seed completed: {Mappings} device-asset mappings, {DisabledSkus} disabled SKUs; " +
            "validator checked {CheckedMappings}/{CheckedDisabled}.",
            foundation.DeviceAssetMappingsWritten,
            foundation.DisabledSkusWritten,
            foundation.Validation.DeviceAssetMappingsChecked,
            foundation.Validation.DisabledSkusChecked);

        // L1「规则排程」块：历次排程运行。分配只引用已落库的工序任务，故排在工单链之后。
        var scheduleResults = await scope.ServiceProvider.GetRequiredService<WorldHistoryScheduleResultSeedService>().SeedAsync(
            leaderDemoOrganizationId,
            leaderDemoEnvironmentId,
            WorldHistoryConfiguration.ResolveScale(builder.Configuration));
        app.Logger.LogInformation(
            "World-history MES schedule-result seed completed: {ScheduleResults} schedule runs; validator checked {Checked}.",
            scheduleResults.ScheduleResultsWritten,
            scheduleResults.Validation.ScheduleResultsChecked);
    }
}

await app.RunAsync();

static string ToLowerCamelEndpointName(string endpointTypeName)
{
    var name = endpointTypeName.EndsWith("Endpoint", StringComparison.Ordinal)
        ? endpointTypeName[..^"Endpoint".Length]
        : endpointTypeName;

    return char.ToLowerInvariant(name[0]) + name[1..];
}

static IReadOnlyCollection<string>? ResolveSiteCodes(IConfiguration configuration)
{
    var sectionValues = configuration.GetSection("Inventory:SiteCodes")
        .Get<string[]>()
        ?.Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .ToArray();
    if (sectionValues is { Length: > 0 })
    {
        return sectionValues;
    }

    var delimited = configuration["Inventory:SiteCodes"];
    if (string.IsNullOrWhiteSpace(delimited))
    {
        return null;
    }

    var values = delimited
        .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    return values.Length == 0 ? null : values;
}

/// <summary>
/// MES web application entry point.
/// </summary>
public partial class Program;
