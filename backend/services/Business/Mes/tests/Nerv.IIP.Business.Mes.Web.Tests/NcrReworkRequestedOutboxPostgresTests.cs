using DotNetCore.CAP;
using DotNetCore.CAP.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class NcrReworkRequestedOutboxPostgresTests
{
    [MesRealPostgresFact]
    public async Task Rework_request_commits_business_facts_inbox_and_created_receipt_outbox_together()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(factory.Services, "org-001", "env-dev");

        var integrationEvent = NcrReworkRequestedPostgresFixtures.CreateEvent();
        using (var handlingScope = factory.Services.CreateScope())
        {
            await handlingScope.ServiceProvider
                .GetRequiredService<NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder>()
                .HandleAsync(integrationEvent, CancellationToken.None);
            Assert.Null(handlingScope.ServiceProvider.GetRequiredService<ITransactionUnitOfWork>().CurrentTransaction);
        }

        using var assertionScope = factory.Services.CreateScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rework = await db.WorkOrders.AsNoTracking().SingleAsync(x => x.SourceNcrId == "ncr-001");
        Assert.Equal(WorkOrder.ReworkType, rework.WorkOrderType);
        Assert.Equal(WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus, rework.MaterialRequirementSnapshotStatus);
        Assert.Equal(2, await db.OperationTasks.CountAsync(x => x.WorkOrderId == rework.WorkOrderIdValue));
        Assert.Single(await db.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
            .ToArrayAsync());
        Assert.Equal(
            rework.WorkOrderIdValue,
            (await db.CodeIdempotencyKeys.SingleAsync(x => x.IdempotencyKey == integrationEvent.IdempotencyKey)).Code);

        var receipt = Assert.Single(
            await ReadReworkReceiptOutboxAsync(),
            row => row.Content.Contains("\"SourceNcrId\":\"ncr-001\"", StringComparison.Ordinal));
        Assert.Contains(nameof(Nerv.IIP.Contracts.Mes.ReworkWorkOrderCreatedIntegrationEvent), receipt.Name, StringComparison.Ordinal);
        Assert.Contains($"\"ReworkWorkOrderId\":\"{rework.WorkOrderIdValue}\"", receipt.Content, StringComparison.Ordinal);
    }

    [MesRealPostgresFact]
    public async Task Rework_receipt_outbox_failure_rolls_back_business_facts_inbox_numbering_and_route()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(factory.Services, "org-001", "env-dev");
        await InstallReworkReceiptOutboxFailureTriggerAsync();

        var integrationEvent = NcrReworkRequestedPostgresFixtures.CreateEvent();
        using (var handlingScope = factory.Services.CreateScope())
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => handlingScope.ServiceProvider
                .GetRequiredService<NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder>()
                .HandleAsync(integrationEvent, CancellationToken.None));
            Assert.Contains("injected rework receipt outbox failure", exception.ToString(), StringComparison.Ordinal);
            Assert.Null(handlingScope.ServiceProvider.GetRequiredService<ITransactionUnitOfWork>().CurrentTransaction);
        }

        using var assertionScope = factory.Services.CreateScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());
        Assert.Empty(await db.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
            .ToArrayAsync());
        Assert.Empty(await db.CodeIdempotencyKeys
            .Where(x => x.IdempotencyKey == integrationEvent.IdempotencyKey)
            .ToArrayAsync());
        Assert.Equal(3, await db.OperationTasks.CountAsync());
        Assert.Empty(await ReadReworkReceiptOutboxAsync());
    }

    [MesRealPostgresFact]
    public async Task Existing_transaction_keeps_rework_facts_uncommitted_until_the_outer_owner_decides()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(factory.Services, "org-001", "env-dev");

        using (var handlingScope = factory.Services.CreateScope())
        {
            var unitOfWork = handlingScope.ServiceProvider.GetRequiredService<ITransactionUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            unitOfWork.CurrentTransaction = transaction;
            try
            {
                await handlingScope.ServiceProvider
                    .GetRequiredService<NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder>()
                    .HandleAsync(NcrReworkRequestedPostgresFixtures.CreateEvent(), CancellationToken.None);

                Assert.Equal(transaction.TransactionId, Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>(
                    unitOfWork.CurrentTransaction).TransactionId);
                Assert.Single(await handlingScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());

                using var observerScope = factory.Services.CreateScope();
                Assert.Empty(await observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());
            }
            finally
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                unitOfWork.CurrentTransaction = null;
            }
        }

        using var assertionScope = factory.Services.CreateScope();
        Assert.Empty(await assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());
        Assert.Empty(await ReadReworkReceiptOutboxAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "InMemory",
            ["Cap:Version"] = "test-mes-rework-uow",
            ["InternalService:BearerToken"] = "test-internal-token",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
                services.AddScoped<IMesMaterialRequirementSnapshotProvider>(_ => NoRequirementsSnapshotProvider.Instance));
        });
    }

    private static async Task InitializeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
    }

    private static async Task<(string Name, string Content)[]> ReadReworkReceiptOutboxAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Name\", \"Content\" FROM cap.published WHERE \"Content\" LIKE '%ReworkWorkOrderCreated%'";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Name, string Content)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows.ToArray();
    }

    private static async Task InstallReworkReceiptOutboxFailureTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE FUNCTION cap.reject_rework_receipt_outbox()
            RETURNS trigger AS $$
            BEGIN
                IF NEW."Content" LIKE '%ReworkWorkOrderCreated%' THEN
                    RAISE EXCEPTION 'injected rework receipt outbox failure';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_rework_receipt_outbox
            BEFORE INSERT ON cap.published
            FOR EACH ROW EXECUTE FUNCTION cap.reject_rework_receipt_outbox();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoRequirementsSnapshotProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoRequirementsSnapshotProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}
