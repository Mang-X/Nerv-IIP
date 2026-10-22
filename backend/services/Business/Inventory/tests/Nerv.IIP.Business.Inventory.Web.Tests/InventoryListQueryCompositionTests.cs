using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryListQueryCompositionTests
{
    [Fact]
    public void Directory_query_composes_normalized_tenant_page_and_keyword_without_mutating_public_fields()
    {
        var query = new ListInventoryDirectoryQuery(
            " org-001 ",
            " env-dev ",
            InventoryDirectoryTypes.Batch,
            Keyword: "  LoT-01 ",
            Skip: 2,
            Take: 25);

        var tenant = TenantScope.From(query.OrganizationId, query.EnvironmentId);
        var page = OffsetPage.From(query.Skip, query.Take);
        var keyword = SearchTerm.From(query.Keyword);

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal(2, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal("lot-01", keyword.Value);
        Assert.Equal(" org-001 ", query.OrganizationId);
        Assert.Equal("  LoT-01 ", query.Keyword);
    }

    [Fact]
    public void Directory_page_keeps_existing_defaults_and_bounds()
    {
        var query = new ListInventoryDirectoryQuery("org-001", "env-dev", InventoryDirectoryTypes.Location);
        var request = new ListInventoryDirectoryRequest("org-001", "env-dev", InventoryDirectoryTypes.Location);

        Assert.Equal((0, 50), (query.Skip, query.Take));
        Assert.Equal((0, 50), (request.Skip, request.Take));
        Assert.Equal((0, 1), (OffsetPage.From(-1, 0).Skip, OffsetPage.From(-1, 0).Take));
        Assert.Equal((0, 200), (OffsetPage.From(0, 201).Skip, OffsetPage.From(0, 201).Take));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_term_treats_blank_keyword_as_absent(string? keyword)
    {
        Assert.Null(SearchTerm.From(keyword).Value);
    }

    [Theory]
    [InlineData("", "env-dev", 0, 50)]
    [InlineData("org-001", "", 0, 50)]
    [InlineData("org-001", "env-dev", -1, 50)]
    [InlineData("org-001", "env-dev", 0, 0)]
    [InlineData("org-001", "env-dev", 0, 201)]
    public void Directory_validator_keeps_tenant_and_page_rejections(
        string organizationId,
        string environmentId,
        int skip,
        int take)
    {
        var result = new ListInventoryDirectoryQueryValidator().Validate(
            new ListInventoryDirectoryQuery(
                organizationId,
                environmentId,
                InventoryDirectoryTypes.Location,
                Skip: skip,
                Take: take));

        Assert.False(result.IsValid);
    }
}
