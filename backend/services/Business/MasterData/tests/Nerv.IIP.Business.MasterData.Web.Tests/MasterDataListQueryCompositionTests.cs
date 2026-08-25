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
            Keyword: "  pump ");

        var criteria = query.ToCriteria();

        Assert.Equal("org-001", criteria.Tenant.OrganizationId);
        Assert.Equal("env-dev", criteria.Tenant.EnvironmentId);
        Assert.Equal(2, criteria.Page.Skip);
        Assert.Equal(25, criteria.Page.Take);
        Assert.Equal("pump", criteria.Keyword.Value);
        Assert.Equal(" org-001 ", query.OrganizationId);
        Assert.Equal("  pump ", query.Keyword);
    }

    [Fact]
    public void List_query_rules_keep_default_and_existing_bounds()
    {
        var validator = new ListMasterDataResourcesQueryValidator();

        Assert.True(validator.Validate(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku")).IsValid);
        Assert.True(validator.Validate(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Skip: 0, Take: 500)).IsValid);
        Assert.False(validator.Validate(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Skip: -1)).IsValid);
        Assert.False(validator.Validate(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Take: 0)).IsValid);
        Assert.False(validator.Validate(new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Take: 501)).IsValid);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void List_query_rules_treat_blank_keyword_as_absent(string? keyword, string expected)
    {
        var criteria = new ListMasterDataResourcesQuery("org-001", "env-dev", "sku", Keyword: keyword).ToCriteria();

        Assert.Equal(expected, criteria.Keyword.Value ?? string.Empty);
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
