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

        // PropertyName 用 OrdinalIgnoreCase 比对：app.UseFastEndpoints(...) 启动时会把
        // ValidatorOptions.Global.PropertyNameResolver 换成 camelCase 解析器且不还原，同程序集里只要有
        // 用例先启动过 host，这里拿到的就是 camelCase 名（#3342）。容忍的只有这一维进程级大小写差异——
        // 成员名写成别的成员或不存在的名字照样红（#3342 的变异矩阵为此各跑了一格）。
        // 这几条规则用的是 FluentValidation 默认文案，文案里嵌的正是同一个受解析器影响的展示名，
        // 所以这里不能像 #3342 其余位点那样改断 ErrorMessage。
        Assert.Contains(result.Errors, error => string.Equals(error.PropertyName, nameof(request.Skip), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => string.Equals(error.PropertyName, nameof(request.Take), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => string.Equals(error.PropertyName, nameof(request.GateStatus), StringComparison.OrdinalIgnoreCase));
    }
}
