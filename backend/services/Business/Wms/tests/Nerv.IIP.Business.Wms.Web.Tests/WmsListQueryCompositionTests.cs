using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsListQueryCompositionTests
{
    // Contract: PublicContract + Regression. Authority: Issue #2120 acceptance and pre-migration WMS v1 behavior.
    [Theory]
    [InlineData(-1, 0, 0, 100)]
    [InlineData(0, 501, 0, 500)]
    public void Offset_page_keeps_legacy_clamp_semantics(
        int skip,
        int take,
        int expectedSkip,
        int expectedTake)
    {
        var page = OffsetPage.From(skip, take);

        Assert.Equal(expectedSkip, page.Skip);
        Assert.Equal(expectedTake, page.Take);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_term_treats_blank_keywords_as_absent(string? keyword)
    {
        Assert.Null(ListQueryCriteria.NormalizeKeyword(keyword));
    }

    [Fact]
    public void Tenant_scope_trims_ids_and_preserves_missing_scope_as_no_match()
    {
        var tenant = TenantScope.From(" org-001 ", " env-dev ");
        var missing = TenantScope.From(" ", "env-dev");

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Null(missing.OrganizationId);
        Assert.Equal("env-dev", missing.EnvironmentId);
    }

    [Fact]
    public void Receiving_quality_validator_keeps_page_and_domain_limits()
    {
        var request = new ListReceivingQualityGatesRequest(
            "org-001",
            "env-dev",
            "worker-001",
            ["SITE-01"],
            "self",
            "worker-001",
            Skip: -1,
            Take: 0,
            GateStatus: new string('x', 51));
        var validator = new ListReceivingQualityGatesRequestValidator();

        var result = validator.Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.Skip));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.Take));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.GateStatus));
    }
}
