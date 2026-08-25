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
            ToolingAsset.Register("org-002", "env-dev", "TOOL-ORG", "其他组织工装", "mould", ["WC-01"], ["SKU-A"], null),
            ToolingAsset.Register("org-001", "env-prod", "TOOL-ENV", "其他环境工装", "mould", ["WC-01"], ["SKU-A"], null));
        await dbContext.SaveChangesAsync();

        var response = await new ListToolingAssetsQueryHandler(dbContext).Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", "mould", ToolingAssetStatus.Available, 0, 100),
            CancellationToken.None);
        var nameResponse = await new ListToolingAssetsQueryHandler(dbContext).Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", "冲压", ToolingAssetStatus.Available, 0, 100),
            CancellationToken.None);
        var codeResponse = await new ListToolingAssetsQueryHandler(dbContext).Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", "tool-002", ToolingAssetStatus.Available, 0, 100),
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
        Assert.Equal("TOOL-002", Assert.Single(nameResponse.Items).Code);
        Assert.Equal("TOOL-002", Assert.Single(codeResponse.Items).Code);
    }

    [Fact]
    public async Task List_filters_status_and_uses_ascending_code_order_for_stable_paging()
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
            new ListToolingAssetsQuery("org-001", "env-dev", null, null, 0, 2),
            CancellationToken.None);
        var retiredPage = await handler.Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", null, ToolingAssetStatus.Retired, 0, 10),
            CancellationToken.None);
        var maintenancePage = await handler.Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", null, ToolingAssetStatus.Maintenance, 0, 10),
            CancellationToken.None);

        Assert.Equal(3, page.Total);
        Assert.Equal(["TOOL-001", "TOOL-002"], page.Items.Select(item => item.Code));
        Assert.False(Assert.Single(retiredPage.Items).IsSchedulable);
        Assert.False(Assert.Single(maintenancePage.Items).IsSchedulable);
    }

    [Fact]
    public async Task List_returns_zero_total_and_no_items_when_filters_match_nothing()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ToolingAssets.Add(
            ToolingAsset.Register("org-001", "env-dev", "TOOL-001", "冲压模具", "mould", ["WC-01"], ["SKU-A"], null));
        await dbContext.SaveChangesAsync();

        var response = await new ListToolingAssetsQueryHandler(dbContext).Handle(
            new ListToolingAssetsQuery("org-001", "env-dev", "不存在的工装", ToolingAssetStatus.Available, 0, 10),
            CancellationToken.None);

        Assert.Equal(0, response.Total);
        Assert.Empty(response.Items);
    }

    [Fact]
    public void Validator_accepts_keyword_skip_and_take_boundary_values()
    {
        var validator = new ListToolingAssetsQueryValidator();

        var result = validator.Validate(new ListToolingAssetsQuery(
            "org-001", "env-dev", new string('x', 200), ToolingAssetStatus.Retired, 0, 500));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validator_reports_the_specific_rule_for_each_invalid_property()
    {
        var validator = new ListToolingAssetsQueryValidator();

        var result = validator.Validate(new ListToolingAssetsQuery("", "", new string('x', 201), (ToolingAssetStatus)99, -1, 501));

        Assert.Equal(6, result.Errors.Count);
        AssertFailure(result, nameof(ListToolingAssetsQuery.OrganizationId), "组织标识不能为空。");
        AssertFailure(result, nameof(ListToolingAssetsQuery.EnvironmentId), "环境标识不能为空。");
        AssertFailure(result, nameof(ListToolingAssetsQuery.Keyword), "关键字不能超过 200 个字符。");
        AssertFailure(result, nameof(ListToolingAssetsQuery.Status), "工装状态无效。");
        AssertFailure(result, nameof(ListToolingAssetsQuery.Skip), "skip 不能小于 0。");
        AssertFailure(result, nameof(ListToolingAssetsQuery.Take), "take 必须在 1 至 500 之间。");
    }

    private static void AssertFailure(
        FluentValidation.Results.ValidationResult result,
        string propertyName,
        string errorMessage)
    {
        var failure = Assert.Single(result.Errors, failure => failure.PropertyName == propertyName);
        Assert.Equal(errorMessage, failure.ErrorMessage);
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
