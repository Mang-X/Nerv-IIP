using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.BarcodeRules;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Resolutions;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Scans;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelListQueryCompositionTests
{
    [Fact]
    public void List_query_criteria_normalize_tenant_page_and_keyword_without_mutating_public_request_fields()
    {
        var query = new ListBarcodeRulesQuery(
            " org-001 ",
            " env-dev ",
            null,
            "  PuMp ",
            Skip: 2,
            Take: 25);

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
    public void List_query_criteria_keep_the_approved_page_bounds()
    {
        Assert.Equal((0, 1), (OffsetPage.From(-1, 0).Skip, OffsetPage.From(-1, 0).Take));
        Assert.Equal((0, 500), (OffsetPage.From(0, 501).Skip, OffsetPage.From(0, 501).Take));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_term_treats_blank_keywords_as_absent(string? keyword)
    {
        Assert.Null(SearchTerm.From(keyword).Value);
    }

    [Fact]
    public void List_query_records_keep_the_existing_default_page()
    {
        Assert.Equal(0, new ListBarcodeRulesQuery("org-001", "env-dev", null, null).Skip);
        Assert.Equal(100, new ListBarcodeRulesQuery("org-001", "env-dev", null, null).Take);
        Assert.Equal(100, new ListLabelTemplatesQuery("org-001", "env-dev", null).Take);
        Assert.Equal(100, new ListLabelPrintBatchesQuery("org-001", "env-dev", null, null, null).Take);
        Assert.Equal(100, new ListScansQuery("org-001", "env-dev", null, null, null, null).Take);
        Assert.Equal(0, new ResolveBarcodeQuery("org-001", "env-dev", "BC001").Skip);
        Assert.Equal(20, new ResolveBarcodeQuery("org-001", "env-dev", "BC001").Take);
    }

    [Fact]
    public void List_query_validators_keep_tenant_presence_and_domain_limits()
    {
        var invalidQueries = new object[]
        {
            new ListBarcodeRulesQuery("", "env-dev", null, null),
            new ListLabelTemplatesQuery("org-001", "", null),
            new ListLabelPrintBatchesQuery("", "env-dev", null, null, null),
            new ListScansQuery("org-001", "", null, null, null, null),
            new ResolveBarcodeQuery("", "env-dev", "BC001"),
        };

        Assert.False(new ListBarcodeRulesQueryValidator().Validate((ListBarcodeRulesQuery)invalidQueries[0]).IsValid);
        Assert.False(new ListLabelTemplatesQueryValidator().Validate((ListLabelTemplatesQuery)invalidQueries[1]).IsValid);
        Assert.False(new ListLabelPrintBatchesQueryValidator().Validate((ListLabelPrintBatchesQuery)invalidQueries[2]).IsValid);
        Assert.False(new ListScansQueryValidator().Validate((ListScansQuery)invalidQueries[3]).IsValid);
        Assert.False(new ResolveBarcodeQueryValidator().Validate((ResolveBarcodeQuery)invalidQueries[4]).IsValid);

        var oversizedKeyword = new ListBarcodeRulesQuery(
            "org-001",
            "env-dev",
            null,
            new string('x', 101));
        Assert.False(new ListBarcodeRulesQueryValidator().Validate(oversizedKeyword).IsValid);
    }

    [Fact]
    public void List_query_validators_keep_legacy_page_boundaries()
    {
        var validator = new ListBarcodeRulesQueryValidator();

        Assert.True(validator.Validate(new ListBarcodeRulesQuery("org-001", "env-dev", null, null, 0, 1)).IsValid);
        Assert.True(validator.Validate(new ListBarcodeRulesQuery("org-001", "env-dev", null, null, 0, 500)).IsValid);
        Assert.False(validator.Validate(new ListBarcodeRulesQuery("org-001", "env-dev", null, null, -1, 1)).IsValid);
        Assert.False(validator.Validate(new ListBarcodeRulesQuery("org-001", "env-dev", null, null, 0, 0)).IsValid);
        Assert.False(validator.Validate(new ListBarcodeRulesQuery("org-001", "env-dev", null, null, 0, 501)).IsValid);
    }
}
