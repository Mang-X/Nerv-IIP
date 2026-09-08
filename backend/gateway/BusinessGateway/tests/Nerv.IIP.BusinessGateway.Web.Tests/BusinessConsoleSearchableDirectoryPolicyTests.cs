using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleSearchableDirectoryPolicyTests
{
    [Theory]
    [InlineData("personnel", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("team", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("equipment", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("work-center", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("station", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("workshop", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("material", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("priority", BusinessGatewayPermissions.MasterDataResourcesRead)]
    [InlineData("location", BusinessGatewayPermissions.InventoryLedgerRead)]
    [InlineData("batch", BusinessGatewayPermissions.InventoryLedgerRead)]
    [InlineData("serial", BusinessGatewayPermissions.InventoryLedgerRead)]
    [InlineData("defect-code", BusinessGatewayPermissions.QualityInspectionRecordsRead)]
    [InlineData("scrap-reason", BusinessGatewayPermissions.QualityInspectionRecordsRead)]
    [InlineData("downtime-reason", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead)]
    [InlineData("maintenance-reason", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead)]
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
        Assert.Equal(
            "directory-scope-unsupported",
            BusinessConsoleSearchableDirectoryPolicy.ValidateScope("downtime-reason", "self", "user-emp-010"));
        Assert.Null(BusinessConsoleSearchableDirectoryPolicy.ValidateScope("location", "site", "SITE-A"));
    }

    // #3125：范围维度的有无决定了「受限 grant 是否可表示」，两侧必须同时被钉住。
    // 有范围维度的目录上，无法表达成过滤条件的 grant 仍然 fail closed（不退化为组织级）；
    // 无范围维度的目录上没有可被越权读到的按范围切分的行，同一条 grant 得到整份词表。
    [Theory]
    [InlineData("location", false)]
    [InlineData("personnel", false)]
    [InlineData("downtime-reason", true)]
    [InlineData("maintenance-reason", true)]
    [InlineData("defect-code", true)]
    [InlineData("scrap-reason", true)]
    [InlineData("material", true)]
    [InlineData("priority", true)]
    public void Self_scoped_grant_is_representable_only_on_directories_without_a_scope_dimension(
        string directoryType,
        bool expectAuthorized)
    {
        var definition = BusinessConsoleSearchableDirectoryPolicy.Require(directoryType);
        var authorization = new BusinessGatewayAuthorizationResult(
            true,
            "user-emp-010",
            "user",
            "emp010",
            null,
            ScopeGrants:
            [
                new AuthorizationScopeGrant(
                    "membership",
                    "user-emp-010:org-001:env-dev",
                    "self",
                    "user-emp-010",
                    [definition.PermissionCode]),
            ]);

        var scope = BusinessConsoleSearchableDirectoryPolicy.ResolveAuthorizedScope(
            definition,
            authorization,
            "org-001",
            null,
            null);

        if (!expectAuthorized)
        {
            Assert.Null(scope);
            return;
        }

        Assert.NotNull(scope);
        Assert.Null(scope.Kind);
        Assert.Null(scope.Id);
    }

    // 端点侧 ValidateScope 已把显式范围挡成 400，policy 这条防线在 HTTP 面不可达；
    // 直接在 policy 上钉住它，避免「无范围维度」被误读成「接受并忽略范围」。
    [Fact]
    public void Explicit_scope_is_never_silently_ignored_on_a_directory_without_a_scope_dimension()
    {
        var definition = BusinessConsoleSearchableDirectoryPolicy.Require("downtime-reason");
        var authorization = new BusinessGatewayAuthorizationResult(
            true,
            "user-emp-010",
            "user",
            "emp010",
            null,
            ScopeGrants:
            [
                new AuthorizationScopeGrant(
                    "membership",
                    "user-emp-010:org-001:env-dev",
                    "self",
                    "user-emp-010",
                    [definition.PermissionCode]),
            ]);

        Assert.Null(BusinessConsoleSearchableDirectoryPolicy.ResolveAuthorizedScope(
            definition,
            authorization,
            "org-001",
            "self",
            "user-emp-010"));
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
