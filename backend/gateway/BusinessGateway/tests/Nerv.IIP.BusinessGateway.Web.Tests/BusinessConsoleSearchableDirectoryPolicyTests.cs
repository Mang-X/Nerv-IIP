using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleSearchableDirectoryPolicyTests
{
    [Theory]
    [InlineData("personnel", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("location", BusinessGatewayPermissions.InventoryLedgerRead)]
    [InlineData("scrap-reason", BusinessGatewayPermissions.QualityInspectionRecordsRead)]
    [InlineData("downtime-reason", BusinessGatewayPermissions.MaintenanceWorkOrdersRead)]
    public void Directory_type_resolves_only_its_owner_permission(string directoryType, string permission)
    {
        var definition = BusinessConsoleSearchableDirectoryPolicy.Require(directoryType);

        Assert.Equal(permission, definition.PermissionCode);
    }

    [Fact]
    public void Scope_must_be_complete_and_supported_by_the_directory_type()
    {
        Assert.Equal(
            "directory-scope-incomplete",
            BusinessConsoleSearchableDirectoryPolicy.ValidateScope("location", "site", null));
        Assert.Equal(
            "directory-scope-unsupported",
            BusinessConsoleSearchableDirectoryPolicy.ValidateScope("priority", "site", "SITE-A"));
        Assert.Null(BusinessConsoleSearchableDirectoryPolicy.ValidateScope("location", "site", "SITE-A"));
    }

    [Fact]
    public void Unknown_directory_type_fails_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BusinessConsoleSearchableDirectoryPolicy.Require("work-order-status"));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("recent")]
    [InlineData("suggested")]
    public void Supported_ranking_modes_are_explicit(string rankingMode)
    {
        Assert.Null(BusinessConsoleSearchableDirectoryPolicy.ValidateRankingMode(rankingMode));
    }

    [Fact]
    public void Recent_and_suggested_without_authority_use_an_explainable_fallback()
    {
        var item = new BusinessConsoleSearchableDirectoryItem(
            "TEAM-A",
            "Team A",
            "TEAM-A",
            "master-data",
            new Dictionary<string, string?>());

        var response = BusinessConsoleSearchableDirectoryResponse.FromItems(
            "team",
            [item],
            1,
            "master-data",
            rankingMode: "suggested");

        Assert.Equal("suggested", response.RankingMode);
        Assert.Equal("unavailable", response.RankingStatus);
        Assert.Equal("directory-ranking-facts-unavailable", response.RankingReasonCode);
        Assert.Equal("code-ascending", response.FallbackOrdering);
        Assert.Same(item, Assert.Single(response.Items));
    }

    [Fact]
    public void Unconfigured_priority_is_explicitly_unavailable()
    {
        var response = BusinessConsoleSearchableDirectoryResponse.FromItems(
            "priority",
            [],
            0,
            "master-data",
            authorityConfigured: false);

        Assert.Equal("unavailable", response.Status);
        Assert.Equal("directory-authority-unconfigured", response.ReasonCode);
        Assert.Empty(response.Items);
        Assert.Equal("code-ascending", response.Ordering);
        Assert.Contains("no ranking affects business decisions", response.OrderingExplanation, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(BusinessConsoleSearchableDirectoryItem).GetProperties(),
            property => property.Name.Contains("score", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("quantity", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("measurement", StringComparison.OrdinalIgnoreCase));
    }
}
