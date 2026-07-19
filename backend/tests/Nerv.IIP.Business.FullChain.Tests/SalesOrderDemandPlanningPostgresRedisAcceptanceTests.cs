using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed class SalesOrderDemandPlanningPostgresRedisAcceptanceTests
{
    [RealPostgresRedisSalesOrderFact]
    public async Task External_process_injects_duplicate_and_out_of_order_sales_order_events()
    {
        var postgres = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var redis = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")!;
        var capVersion = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION")!;
        var salesOrderId = Environment.GetEnvironmentVariable("NERV_IIP_TEST_SALES_ORDER_ID")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = redis,
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(SalesOrderDemandPlanningPostgresRedisAcceptanceTests).Assembly));
        services.AddDbContext<DemandPlanningDbContext>(options => options.UseNpgsql(
            postgres,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DemandPlanningFacts.Schema)));
        services.AddCap(options =>
        {
            options.Version = capVersion;
            options.UseEntityFramework<DemandPlanningDbContext>();
            options.UseConfiguredTransport(configuration, "Development");
        });

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
        var publisher = provider.GetRequiredService<ICapPublisher>();
        await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), Changed(salesOrderId, 3, 999m, "duplicate-v3"));
        await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), Changed(salesOrderId, 2, 888m, "out-of-order-v2"));
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    private static SalesOrderChangedIntegrationEvent Changed(string salesOrderId, int version, decimal quantity, string eventSuffix) =>
        new(
            $"evt-probe-{eventSuffix}",
            ErpIntegrationEventTypes.SalesOrderChanged,
            ErpIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            ErpIntegrationEventSources.BusinessErp,
            "corr-man517-cross-process",
            $"probe-{eventSuffix}",
            "org-001",
            "env-dev",
            "system:acceptance-probe",
            $"erp:sales-order:org-001:env-dev:SO-DEMO-001:v{version}:changed",
            new SalesOrderLifecyclePayload(
                salesOrderId,
                "SO-DEMO-001",
                "CUST-DEMO-001",
                "SITE-001",
                version,
                "released",
                [new SalesOrderLineSnapshot("10", "SKU-DEMO-001", quantity, "EA", new DateOnly(2026, 8, 15), false)]));
}

internal sealed class RealPostgresRedisSalesOrderFactAttribute : FactAttribute
{
    public RealPostgresRedisSalesOrderFactAttribute()
    {
        var required = new[]
        {
            "NERV_IIP_TEST_POSTGRES",
            "NERV_IIP_TEST_REDIS",
            "NERV_IIP_TEST_CAP_VERSION",
            "NERV_IIP_TEST_SALES_ORDER_ID",
        };
        if (required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Set the MAN-517 PostgreSQL, Redis, CAP version, and sales-order id variables to run the external-process fault injector.";
        }
    }
}
