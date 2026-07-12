using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.OeeProductionFactAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

public sealed class IndustrialTelemetryOeePostgresQueryTests
{
    [RealPostgresFact]
    public async Task Oee_query_filters_production_facts_by_datetimeoffset_window_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        await using var database = await IndustrialTelemetryPostgresTestDatabase.CreateAsync(connectionString);
        await using var dbContext = database.CreateContext();
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
}
