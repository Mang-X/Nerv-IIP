using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(IndustrialTelemetryPostgresLaneDatabase.CollectionName)]
public sealed class IndustrialTelemetryOeePostgresQueryTests
{
    [RealPostgresFact]
    public async Task Oee_query_filters_production_facts_by_datetimeoffset_window_on_postgres()
    {
        await IndustrialTelemetryPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = CreateLaneDbContext();
        IndustrialTelemetryPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        dbContext.OeeProductionFacts.AddRange(
            Fact("PRPT-OEE-PG-BEFORE", "2026-07-10T07:59:59Z"),
            Fact("PRPT-OEE-PG-IN", "2026-07-10T08:30:00Z"),
            Fact("PRPT-OEE-PG-END", "2026-07-10T10:00:00Z"));
        await dbContext.SaveChangesAsync();

        var result = await new QueryOeeQueryHandler(dbContext).Handle(
            new QueryOeeQuery(
                "org-001",
                "env-dev",
                "DEV-OEE-PG-01",
                DateTimeOffset.Parse("2026-07-10T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-10T10:00:00Z")),
            CancellationToken.None);

        Assert.Equal(1, result.ProductionFactCount);
        Assert.Equal(10m, result.GoodQuantity);
    }

    private static ApplicationDbContext CreateLaneDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                IndustrialTelemetryPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "industrial_telemetry"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static OeeProductionFact Fact(string reportNo, string reportedAtUtc) =>
        OeeProductionFact.Project(
            "org-001",
            "env-dev",
            reportNo,
            "WC-OEE-PG-01",
            "DEV-OEE-PG-01",
            10m,
            0m,
            0m,
            "PCS",
            10m,
            DateTimeOffset.Parse(reportedAtUtc));

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
