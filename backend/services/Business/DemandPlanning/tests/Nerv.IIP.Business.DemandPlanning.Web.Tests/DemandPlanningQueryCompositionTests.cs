using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

// Contract: Regression. Authority: Issue #2128 acceptance.
public sealed class DemandPlanningQueryCompositionTests
{
    [Fact]
    public async Task List_demand_sources_composes_tenant_keyword_and_offset_page()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.DemandSources.AddRange(
            DemandSource.Create("org-001", "env-dev", "manual", "DEMAND-PUMP-A", "SKU-PUMP-A", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            DemandSource.Create("org-001", "env-dev", "manual", "DEMAND-PUMP-B", "SKU-PUMP-B", "pcs", "SITE-01", 20m, new DateOnly(2026, 6, 2)),
            DemandSource.Create("org-001", "env-dev", "manual", "DEMAND-OTHER", "SKU-OTHER", "pcs", "SITE-01", 30m, new DateOnly(2026, 6, 3)),
            DemandSource.Create("org-002", "env-dev", "manual", "DEMAND-PUMP-OTHER-ORG", "SKU-PUMP-X", "pcs", "SITE-01", 40m, new DateOnly(2026, 6, 4)),
            DemandSource.Create("org-001", "env-test", "manual", "DEMAND-PUMP-OTHER-ENV", "SKU-PUMP-Y", "pcs", "SITE-01", 50m, new DateOnly(2026, 6, 5)));
        await dbContext.SaveChangesAsync();

        var demands = await new ListDemandSourcesQueryHandler(dbContext).Handle(
            new ListDemandSourcesQuery(" org-001 ", " env-dev ", " PuMp ", 1, 1),
            CancellationToken.None);

        var demand = Assert.Single(demands);
        Assert.Equal("DEMAND-PUMP-B", demand.SourceReference);
    }

    [Fact]
    public async Task List_forecast_inputs_composes_tenant_keyword_and_offset_page()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ForecastInputs.AddRange(
            ForecastInput.Create("org-001", "env-dev", "FORECAST-PUMP-A", "SKU-PUMP-A", "pcs", "SITE-01", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 10m, 0, 0),
            ForecastInput.Create("org-001", "env-dev", "FORECAST-PUMP-B", "SKU-PUMP-B", "pcs", "SITE-01", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 20m, 0, 0),
            ForecastInput.Create("org-001", "env-dev", "FORECAST-OTHER", "SKU-OTHER", "pcs", "SITE-01", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 30m, 0, 0),
            ForecastInput.Create("org-002", "env-dev", "FORECAST-PUMP-OTHER-ORG", "SKU-PUMP-X", "pcs", "SITE-01", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 40m, 0, 0),
            ForecastInput.Create("org-001", "env-test", "FORECAST-PUMP-OTHER-ENV", "SKU-PUMP-Y", "pcs", "SITE-01", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 50m, 0, 0));
        await dbContext.SaveChangesAsync();

        var forecasts = await new ListForecastInputsQueryHandler(dbContext).Handle(
            new ListForecastInputsQuery(" org-001 ", " env-dev ", null, null, null, null, " PuMp ", 1, 1),
            CancellationToken.None);

        var forecast = Assert.Single(forecasts);
        Assert.Equal("FORECAST-PUMP-B", forecast.ForecastReference);
    }

    [Fact]
    public void List_query_criteria_normalizes_page_bounds_and_blank_keyword()
    {
        var lowerBound = OffsetPage.From(-1, 0);
        var upperBound = OffsetPage.From(0, 501);

        Assert.Equal(0, lowerBound.Skip);
        Assert.Equal(1, lowerBound.Take);
        Assert.Equal(0, upperBound.Skip);
        Assert.Equal(500, upperBound.Take);
        Assert.Null(SearchTerm.From("  ").Value);
    }

    [Theory]
    [InlineData("", "env-dev", "组织标识不能为空。")]
    [InlineData("org-001", "", "环境标识不能为空。")]
    public void List_demand_sources_validator_rejects_missing_tenant(string organizationId, string environmentId, string expectedMessage)
    {
        var result = new ListDemandSourcesQueryValidator().Validate(
            new ListDemandSourcesQuery(organizationId, environmentId));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == expectedMessage);
    }

    [Theory]
    [InlineData("", "env-dev", "组织标识不能为空。")]
    [InlineData("org-001", "", "环境标识不能为空。")]
    public void List_forecast_inputs_validator_rejects_missing_tenant(string organizationId, string environmentId, string expectedMessage)
    {
        var result = new ListForecastInputsQueryValidator().Validate(
            new ListForecastInputsQuery(organizationId, environmentId, null, null, null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == expectedMessage);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"demand-planning-query-composition-{Guid.NewGuid():N}";
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}
