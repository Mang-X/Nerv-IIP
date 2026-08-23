using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class ToolingAssetDirectoryQueryTests
{
    [Fact]
    public async Task List_returns_persisted_tooling_facts_and_applicability_for_requested_scope()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var matching = ToolingAsset.Register(
            "org-001", "env-dev", "TOOL-002", "冲压模具", "mould", ["WC-02", "WC-01"], ["SKU-B", "SKU-A"], 10);
        matching.RecordUsage(3);
        dbContext.ToolingAssets.AddRange(
            matching,
            ToolingAsset.Register("org-002", "env-dev", "TOOL-002", "其他组织", "mould", ["WC-01"], ["SKU-A"], null),
            ToolingAsset.Register("org-001", "env-prod", "TOOL-002", "其他环境", "mould", ["WC-01"], ["SKU-A"], null));
        await dbContext.SaveChangesAsync();

        var response = await new ListToolingAssetsQueryHandler(dbContext).Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", "冲压", ToolingAssetStatus.Available, 0, 100),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("TOOL-002", item.Code);
        Assert.Equal("冲压模具", item.Name);
        Assert.Equal("mould", item.ToolingType);
        Assert.Equal(ToolingAssetStatus.Available, item.Status);
        Assert.Equal(10, item.MaintenanceLifeCount);
        Assert.Equal(3, item.UsageCount);
        Assert.True(item.IsSchedulable);
        Assert.Equal(["WC-01", "WC-02"], item.WorkCenterCodes);
        Assert.Equal(["SKU-A", "SKU-B"], item.SkuCodes);
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task List_filters_status_and_uses_code_order_for_stable_paging_and_empty_results()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var retired = ToolingAsset.Register("org-001", "env-dev", "TOOL-001", "模具 1", "mould", ["WC-01"], ["SKU-A"], null);
        retired.ChangeStatus(ToolingAssetStatus.Retired, "报废");
        var maintenance = ToolingAsset.Register("org-001", "env-dev", "TOOL-003", "模具 3", "mould", ["WC-01"], ["SKU-A"], 1);
        maintenance.RecordUsage(1);
        dbContext.ToolingAssets.AddRange(
            retired,
            ToolingAsset.Register("org-001", "env-dev", "TOOL-002", "模具 2", "mould", ["WC-01"], ["SKU-A"], null),
            maintenance);
        await dbContext.SaveChangesAsync();

        var handler = new ListToolingAssetsQueryHandler(dbContext);
        var page = await handler.Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", null, null, 1, 1),
            CancellationToken.None);
        var empty = await handler.Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", null, ToolingAssetStatus.Maintenance, 1, 10),
            CancellationToken.None);

        Assert.Equal(3, page.Total);
        Assert.Equal("TOOL-002", Assert.Single(page.Items).Code);
        Assert.Equal(1, empty.Total);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public void Validator_rejects_blank_scope_invalid_status_and_out_of_range_paging()
    {
        var validator = new ListToolingAssetsQueryValidator();

        var result = validator.Validate(new ListToolingAssetsQuery("", "", new string('x', 201), (ToolingAssetStatus)99, -1, 501));

        Assert.Equal(6, result.Errors.Count);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"tooling-directory-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }
}
