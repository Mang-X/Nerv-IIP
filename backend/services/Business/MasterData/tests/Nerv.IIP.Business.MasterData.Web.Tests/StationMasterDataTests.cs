using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.StationAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class StationMasterDataTests
{
    [Fact]
    public async Task Station_directory_lists_station_master_data_even_without_devices_and_inherits_site_from_line()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionLines.Add(ProductionLine.Create(OrganizationId, EnvironmentId, "LINE-A", "Line A", "SITE-1", "WS-1"));
        dbContext.Stations.Add(Station.Create(OrganizationId, EnvironmentId, "ST-NEW", "新装配工位", "LINE-A", "WC-A"));
        dbContext.Stations.Add(Station.Create(OrganizationId, EnvironmentId, "ST-LEGACY", "ST-LEGACY", "LINE-UNREGISTERED"));
        var disabled = Station.Create(OrganizationId, EnvironmentId, "ST-OFF", "停用工位", "LINE-A");
        disabled.Disable("retired");
        dbContext.Stations.Add(disabled);
        dbContext.Stations.Add(Station.Create("org-other", EnvironmentId, "ST-FOREIGN", "Foreign", "LINE-A"));
        await dbContext.SaveChangesAsync();
        var handler = new ListMasterDataResourcesQueryHandler(dbContext);

        var all = await handler.Handle(new ListMasterDataResourcesQuery(OrganizationId, EnvironmentId, "station"), CancellationToken.None);
        Assert.Equal(2, all.Total);
        Assert.Equal(["ST-LEGACY", "ST-NEW"], all.Resources.Select(x => x.Code));
        var created = all.Resources.Single(x => x.Code == "ST-NEW");
        Assert.Equal("新装配工位", created.DisplayName);
        Assert.Equal("ST-NEW", created.StationCode);
        Assert.Equal("SITE-1", created.SiteCode);
        Assert.Equal("WS-1", created.WorkshopCode);
        Assert.Equal("LINE-A", created.LineCode);
        Assert.Equal("WC-A", created.WorkCenterCode);
        var legacy = all.Resources.Single(x => x.Code == "ST-LEGACY");
        Assert.Null(legacy.SiteCode);
        Assert.Equal("LINE-UNREGISTERED", legacy.LineCode);

        var byName = await handler.Handle(new ListMasterDataResourcesQuery(OrganizationId, EnvironmentId, "station", Keyword: "装配"), CancellationToken.None);
        Assert.Equal("ST-NEW", Assert.Single(byName.Resources).Code);
        var byWorkCenter = await handler.Handle(new ListMasterDataResourcesQuery(OrganizationId, EnvironmentId, "station", WorkCenterCode: "WC-A"), CancellationToken.None);
        Assert.Equal("ST-NEW", Assert.Single(byWorkCenter.Resources).Code);
        var includingDisabled = await handler.Handle(new ListMasterDataResourcesQuery(OrganizationId, EnvironmentId, "station", IncludeDisabled: true), CancellationToken.None);
        Assert.Equal(3, includingDisabled.Total);
    }

    [Fact]
    public async Task Create_station_under_enabled_line_is_listed_and_readable_as_detail()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionLines.Add(ProductionLine.Create(OrganizationId, EnvironmentId, "LINE-A", "Line A", "SITE-1"));
        dbContext.WorkCenters.Add(WorkCenter.CreateResource(OrganizationId, EnvironmentId, "WC-A", "Assembly", 480, "work-center", "SITE-1", "LINE-A", null, "CAL-1", "minute", true));
        await dbContext.SaveChangesAsync();

        var result = await CreateHandler(dbContext).Handle(
            new CreateStationCommand(OrganizationId, EnvironmentId, "ST-01", "工位 1", " LINE-A ", "WC-A"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(new MasterDataResourceResult("station", "ST-01", "工位 1"), result);
        var detail = await new GetMasterDataResourceDetailQueryHandler(dbContext).Handle(
            new GetMasterDataResourceDetailQuery(OrganizationId, EnvironmentId, "station", "ST-01"),
            CancellationToken.None);
        Assert.Equal("LINE-A", detail.LineCode);
        Assert.Equal("WC-A", detail.WorkCenterCode);
        Assert.Equal("active", detail.Status);
        await Assert.ThrowsAsync<KnownException>(() => CreateHandler(dbContext).Handle(
            new CreateStationCommand(OrganizationId, EnvironmentId, "ST-01", "重复", "LINE-A"),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("missing-line")]
    [InlineData("disabled-line")]
    [InlineData("missing-work-center")]
    public async Task Create_station_rejects_parent_line_or_work_center_that_is_not_enabled(string scenario)
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (scenario != "missing-line")
        {
            var line = ProductionLine.Create(OrganizationId, EnvironmentId, "LINE-A", "Line A", "SITE-1");
            if (scenario == "disabled-line")
            {
                line.Disable("retired");
            }

            dbContext.ProductionLines.Add(line);
            await dbContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() => CreateHandler(dbContext).Handle(
            new CreateStationCommand(OrganizationId, EnvironmentId, "ST-01", "工位 1", "LINE-A", scenario == "missing-work-center" ? "WC-TYPO" : null),
            CancellationToken.None));

        Assert.Contains(scenario == "missing-work-center" ? "工作中心" : "产线", exception.Message, StringComparison.Ordinal);
        await dbContext.SaveChangesAsync();
        Assert.False(await dbContext.Stations.AnyAsync());
    }

    [Fact]
    public async Task Update_station_rejects_moving_to_a_missing_line_before_mutation()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ProductionLines.Add(ProductionLine.Create(OrganizationId, EnvironmentId, "LINE-A", "Line A", "SITE-1"));
        var station = Station.Create(OrganizationId, EnvironmentId, "ST-01", "工位 1", "LINE-A");
        dbContext.Stations.Add(station);
        await dbContext.SaveChangesAsync();
        var handler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new UpdateMasterDataResourceCommand(OrganizationId, EnvironmentId, "station", "ST-01", Name: "must-not-apply", LineCode: "LINE-TYPO"),
            CancellationToken.None));
        Assert.Equal("工位 1", station.Name);
        Assert.Equal("LINE-A", station.LineCode);

        var renamed = await handler.Handle(
            new UpdateMasterDataResourceCommand(OrganizationId, EnvironmentId, "station", "ST-01", Name: "工位 一"),
            CancellationToken.None);
        Assert.Equal("工位 一", renamed.DisplayName);
    }

    private static CreateStationCommandHandler CreateHandler(ApplicationDbContext dbContext) =>
        new(new StationRepository(dbContext), dbContext);

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"station-master-data-{Guid.CreateVersion7():N}"));
        return services.BuildServiceProvider();
    }

    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
}
