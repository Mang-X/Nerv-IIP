using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries.QualityReasons;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class QualityReasonPostgresProfileTests
{
    [QualityPostgresFact]
    public async Task Scrap_reason_query_filters_scope_search_and_paging_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddQualityPostgreSqlPersistence(QualityPostgresLaneDatabase.ConnectionString);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        dbContext.QualityReasons.AddRange(
            QualityReason.Create("org-001", "env-dev", "SCRAP-SURFACE-B", "Surface B", "Appearance", "major", "scrap", true),
            QualityReason.Create("org-001", "env-dev", "SCRAP-SURFACE-A", "Surface A", "Appearance", "major", "scrap", true),
            QualityReason.Create("org-001", "env-dev", "SCRAP-SURFACE-DISABLED", "Surface Disabled", "Appearance", "major", "scrap", false),
            QualityReason.Create("org-001", "env-dev", "SCRAP-DENT", "Dent", "Appearance", "major", "scrap", true),
            QualityReason.Create("org-001", "env-dev", "REWORK-SURFACE", "Surface Rework", "Appearance", "minor", "rework", true),
            QualityReason.Create("org-001", "env-test", "SCRAP-SURFACE-ENV", "Surface Other Environment", "Appearance", "major", "scrap", true),
            QualityReason.Create("org-002", "env-dev", "SCRAP-SURFACE-ORG", "Surface Other Organization", "Appearance", "major", "scrap", true));
        await dbContext.SaveChangesAsync();

        var firstPage = await new ListScrapQualityReasonCodesQueryHandler(dbContext).Handle(
            new ListScrapQualityReasonCodesQuery("org-001", "env-dev", "surface", Skip: 0, Take: 1),
            CancellationToken.None);

        var secondPage = await new ListScrapQualityReasonCodesQueryHandler(dbContext).Handle(
            new ListScrapQualityReasonCodesQuery("org-001", "env-dev", "surface", Skip: 1, Take: 1),
            CancellationToken.None);

        var firstItem = Assert.Single(firstPage.Items);
        Assert.Equal("SCRAP-SURFACE-A", firstItem.ReasonCode);
        Assert.Equal(2, firstPage.Total);
        Assert.Equal("scrap", firstItem.DefaultDisposition);
        Assert.True(firstItem.Enabled);

        var secondItem = Assert.Single(secondPage.Items);
        Assert.Equal("SCRAP-SURFACE-B", secondItem.ReasonCode);
        Assert.Equal(2, secondPage.Total);
        Assert.DoesNotContain(firstPage.Items, x => x.ReasonCode == "SCRAP-SURFACE-DISABLED");
        Assert.DoesNotContain(firstPage.Items, x => x.ReasonCode == "SCRAP-DENT");

        var emptyPage = await new ListScrapQualityReasonCodesQueryHandler(dbContext).Handle(
            new ListScrapQualityReasonCodesQuery("org-001", "env-dev", "surface", Skip: 2, Take: 1),
            CancellationToken.None);

        Assert.Empty(emptyPage.Items);
        Assert.Equal(2, emptyPage.Total);
    }
}
