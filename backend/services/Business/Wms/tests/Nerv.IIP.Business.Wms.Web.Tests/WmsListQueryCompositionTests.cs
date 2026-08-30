using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Endpoints.Wms;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsListQueryCompositionTests
{
    [Theory]
    [InlineData(-1, 0, 0, 1)]
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
        Assert.Null(SearchTerm.From(keyword).Value);
    }

    [Fact]
    public void Tenant_scope_trims_ids_and_rejects_missing_ids()
    {
        var tenant = TenantScope.From(" org-001 ", " env-dev ");

        Assert.Equal("org-001", tenant.OrganizationId);
        Assert.Equal("env-dev", tenant.EnvironmentId);
        Assert.Equal(
            "组织标识不能为空。",
            Assert.Throws<KnownException>(() => TenantScope.From(" ", "env-dev")).Message);
        Assert.Equal(
            "环境标识不能为空。",
            Assert.Throws<KnownException>(() => TenantScope.From("org-001", " ")).Message);
    }

    [Fact]
    public void Receiving_quality_validator_delegates_page_bounds_and_keeps_domain_limits()
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

        Assert.DoesNotContain(result.Errors, error => error.PropertyName is nameof(request.Skip) or nameof(request.Take));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(request.GateStatus));
    }
}
