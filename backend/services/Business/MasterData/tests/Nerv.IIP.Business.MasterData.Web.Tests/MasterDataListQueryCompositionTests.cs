using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataListQueryCompositionTests
{
    [Fact]
    public void List_query_composes_normalized_tenant_page_and_keyword_without_changing_public_fields()
    {
        var query = new ListMasterDataResourcesQuery(
            " org-001 ",
            " env-dev ",
            "sku",
            Skip: 2,
            Take: 25,
            Keyword: "  PuMp ");

        var tenant = TenantScope.From(query.OrganizationId, query.EnvironmentId);
        var page = OffsetPage.From(query.Skip, query.Take);
        var keyword = SearchTerm.From(query.Keyword);

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal(2, page.Skip);
        Assert.Equal(25, page.Take);
        Assert.Equal("pump", keyword.Value);
        Assert.Equal(" org-001 ", query.OrganizationId);
        Assert.Equal("  PuMp ", query.Keyword);
    }

    [Fact]
    public void List_query_criteria_preserves_legacy_page_clamping()
    {
        Assert.Equal(100, OffsetPage.From(0, 100).Take);
        Assert.Equal(500, OffsetPage.From(0, 500).Take);
        Assert.Equal((0, 1), (OffsetPage.From(-1, 0).Skip, OffsetPage.From(-1, 0).Take));
        Assert.Equal(500, OffsetPage.From(0, 501).Take);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void List_query_rules_treat_blank_keyword_as_absent(string? keyword, string expected)
    {
        var normalized = SearchTerm.From(keyword);

        Assert.Equal(expected, normalized.Value ?? string.Empty);
    }

    [Fact]
    public void Criteria_value_objects_expose_no_public_constructor_bypass()
    {
        Assert.Empty(typeof(TenantScope).GetConstructors());
        Assert.Empty(typeof(OffsetPage).GetConstructors());
        Assert.Empty(typeof(SearchTerm).GetConstructors());
    }

    [Theory]
    [InlineData("", "env-dev")]
    [InlineData("org-001", "")]
    [InlineData("   ", "env-dev")]
    public void List_query_rules_require_both_tenant_values(string organizationId, string environmentId)
    {
        var result = new ListMasterDataResourcesQueryValidator().Validate(
            new ListMasterDataResourcesQuery(organizationId, environmentId, "sku"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName is "OrganizationId" or "EnvironmentId");
    }
}
