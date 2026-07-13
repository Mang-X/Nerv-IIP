using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class BusinessPartnerChangedPostgresAcceptanceTests
{
    [BusinessPartnerPostgresFact]
    public async Task PostgreSQL_consumer_persists_disabled_partner_and_changes_new_PO_SO_behavior()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"),
                x => x.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        await dbContext.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);
        await using (var command = dbContext.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await command.ExecuteNonQueryAsync();
        }

        await dbContext.Database.MigrateAsync();
        var quotation = Quotation.Create(
            "org-pg",
            "env-pg",
            "QT-PG-001",
            "BP-PG-001",
            new DateOnly(2099, 1, 1),
            [new QuotationLineDraft("LINE-001", "SKU-FG", "EA", 1m, 20m, new DateOnly(2099, 1, 31))]);
        quotation.Approve();
        dbContext.Quotations.Add(quotation);
        var changedAtUtc = DateTimeOffset.Parse("2026-07-13T04:00:00Z");
        var integrationEvent = new BusinessPartnerChangedIntegrationEvent(
            "evt-partner-disabled-pg",
            MasterDataIntegrationEventTypes.BusinessPartnerChanged,
            MasterDataIntegrationEventVersions.V1,
            changedAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            "corr-partner-disabled-pg",
            "cause-partner-disabled-pg",
            "org-pg",
            "env-pg",
            "user:masterdata-admin",
            "partner-disabled-pg",
            new MasterDataChangedPayload("business-partner", "BP-PG-001", "disabled", changedAtUtc));

        await new BusinessPartnerChangedIntegrationEventHandlerForProjectAvailability(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        Assert.True((await dbContext.BusinessPartnerAvailabilities.SingleAsync()).IsDisabled);
        Assert.Single(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        await Assert.ThrowsAsync<KnownException>(() => new CreatePurchaseOrderCommandHandler(dbContext).Handle(
            new CreatePurchaseOrderCommand(
                "org-pg",
                "env-pg",
                "PO-PG-001",
                "BP-PG-001",
                "SITE-PG",
                [new PurchaseOrderCommandLine("LINE-001", "SKU-RM", "EA", 1m, 10m, new DateOnly(2099, 1, 31))]),
            CancellationToken.None));
        await Assert.ThrowsAsync<KnownException>(() => new CreateSalesOrderCommandHandler(
            dbContext,
            new StaticCreditProfileReader()).Handle(
                new CreateSalesOrderCommand("org-pg", "env-pg", "SO-PG-001", "QT-PG-001"),
                CancellationToken.None));
    }

    private sealed class StaticCreditProfileReader : ICustomerCreditProfileReader
    {
        public Task<CustomerCreditProfile?> GetAsync(string organizationId, string environmentId, string customerCode, CancellationToken cancellationToken)
        {
            return Task.FromResult<CustomerCreditProfile?>(new CustomerCreditProfile(customerCode, 1_000m, "CNY"));
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class BusinessPartnerPostgresFactAttribute : FactAttribute
{
    public BusinessPartnerPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP business-partner consumer acceptance test.";
        }
    }
}
