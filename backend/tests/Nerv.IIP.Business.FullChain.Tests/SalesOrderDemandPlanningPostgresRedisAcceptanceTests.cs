using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
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
        var probeRunId = Environment.GetEnvironmentVariable("NERV_IIP_TEST_PROBE_RUN_ID")!;
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
        string salesOrderId;
        await using (var sourceScope = provider.CreateAsyncScope())
        {
            var sourceDbContext = sourceScope.ServiceProvider.GetRequiredService<DemandPlanningDbContext>();
            salesOrderId = await sourceDbContext.DemandSources
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == "org-001" &&
                    x.EnvironmentId == "env-dev" &&
                    x.DemandType == "sales-order" &&
                    x.SourceReference == "SO-DEMO-001")
                .Select(x => x.SourceDocumentId)
                .Distinct()
                .SingleAsync();
        }

        var publisher = provider.GetRequiredService<ICapPublisher>();
        var duplicateEvent = Changed(salesOrderId, 3, 999m, $"{probeRunId}-duplicate-v3");
        var staleEvent = Changed(salesOrderId, 2, 888m, $"{probeRunId}-out-of-order-v2");
        await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), duplicateEvent);
        await publisher.PublishAsync(nameof(SalesOrderChangedIntegrationEvent), staleEvent);

        // Real Redis CAP transport: DemandPlanning consumes in its own scope, so the only observable fact is
        // the persisted consumer evidence. Poll it on a bounded budget and report the last observation.
        await Eventually.WaitAsync(
            condition: "DemandPlanning persisted consumer evidence for both injected duplicate/out-of-order events",
            observe: async token =>
            {
                await using var verificationScope = provider.CreateAsyncScope();
                var dbContext = verificationScope.ServiceProvider.GetRequiredService<DemandPlanningDbContext>();
                var consumedEventIds = await dbContext.ProcessedIntegrationEvents
                    .AsNoTracking()
                    .Where(x => x.EventId == duplicateEvent.EventId || x.EventId == staleEvent.EventId)
                    .Select(x => x.EventId)
                    .ToArrayAsync(token);
                return consumedEventIds.Distinct(StringComparer.Ordinal).Count();
            },
            isSatisfied: distinctConsumed => distinctConsumed == 2,
            describe: distinctConsumed => $"distinctConsumedEvents={distinctConsumed}; expected=2",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(45),
                PollInterval: TimeSpan.FromMilliseconds(250),
                SensitiveValues: [postgres, redis]));
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
            $"probe:sales-order:org-001:env-dev:SO-DEMO-001:v{version}:{eventSuffix}",
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
            "NERV_IIP_TEST_PROBE_RUN_ID",
        };
        if (required.Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "Set the MAN-517 PostgreSQL, Redis, CAP version, and probe-run variables to run the external-process fault injector.";
        }
    }
}
