using System.Text.Json;
using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Barcode;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Validation;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract + Regression: #2907 freezes the existing Barcode request boundaries.
public sealed class BusinessConsoleBarcodeRequestCompositionValidationTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 500)]
    [InlineData(2, 200)]
    public void Take_only_composition_preserves_the_callers_inclusive_range(int minimum, int maximum)
    {
        var validator = new InlineValidator<BusinessConsoleBarcodeRuleListRequest>();
        validator.Take(x => x.Take, minimum, maximum);
        var baseline = new BusinessConsoleBarcodeRuleListRequest("org", "env");
        Assert.True(validator.Validate(baseline with { Take = minimum }).IsValid);
        Assert.True(validator.Validate(baseline with { Take = maximum }).IsValid);
        AssertOnlyError(validator, baseline with { Take = minimum - 1 }, "Take");
        AssertOnlyError(validator, baseline with { Take = maximum + 1 }, "Take");
    }

    [Fact]
    public void Barcode_lists_preserve_tenant_and_offset_boundaries()
    {
        VerifyList(new BusinessConsoleBarcodeRuleListRequestValidator(),
            (org, env, skip, take) => new BusinessConsoleBarcodeRuleListRequest(org, env, Skip: skip, Take: take));
        VerifyList(new BusinessConsoleBarcodeTemplateListRequestValidator(),
            (org, env, skip, take) => new BusinessConsoleBarcodeTemplateListRequest(org, env, Skip: skip, Take: take));
        VerifyList(new BusinessConsoleBarcodePrintBatchListRequestValidator(),
            (org, env, skip, take) => new BusinessConsoleBarcodePrintBatchListRequest(org, env, Skip: skip, Take: take));
        VerifyList(new BusinessConsoleBarcodeScanListRequestValidator(),
            (org, env, skip, take) => new BusinessConsoleBarcodeScanListRequest(org, env, Skip: skip, Take: take));
    }

    [Fact]
    public void Barcode_lifecycle_requests_preserve_tenant_boundaries()
    {
        VerifyTenant(new BusinessConsoleDispatchBarcodePrintBatchRequestValidator(),
            (org, env) => new BusinessConsoleDispatchBarcodePrintBatchRequest(
                "batch", org, env, new BusinessConsoleDispatchBarcodePrintBatchBody("batch", "printer")));
        VerifyTenant(new BusinessConsoleReprintBarcodeLabelRequestValidator(),
            (org, env) => new BusinessConsoleReprintBarcodeLabelRequest(
                "batch", 1, org, env, new BusinessConsoleReprintBarcodeLabelBody("batch", 1, "printer")));
        VerifyTenant(new BusinessConsoleVoidBarcodeLabelRequestValidator(),
            (org, env) => new BusinessConsoleVoidBarcodeLabelRequest(
                "batch", 1, org, env, new BusinessConsoleVoidBarcodeLabelBody("batch", 1, "reason")));
    }

    [Fact]
    public void Rule_keyword_is_optional_and_accepts_whitespace_but_not_over_100_characters()
    {
        var validator = new BusinessConsoleBarcodeRuleListRequestValidator();
        var baseline = new BusinessConsoleBarcodeRuleListRequest("org", "env");
        foreach (var keyword in new[] { null, "", "   ", new string('k', 100) })
            Assert.True(validator.Validate(baseline with { Keyword = keyword }).IsValid);
        AssertOnlyError(validator, baseline with { Keyword = new string('k', 101) }, "Keyword");
    }

    [Fact]
    public void Resolve_preserves_tenant_scanned_value_and_page_boundaries()
    {
        var validator = new BusinessConsoleBarcodeResolveRequestValidator();
        var baseline = new BusinessConsoleBarcodeResolveRequest("org", "env", "scan");
        Assert.True(validator.Validate(baseline).IsValid);
        VerifyTenant(validator, (org, env) => baseline with { OrganizationId = org, EnvironmentId = env });
        foreach (var value in new[] { null, "", "   ", new string('s', 201) })
            AssertOnlyError(validator, baseline with { ScannedValue = value! }, "ScannedValue");
        Assert.True(validator.Validate(baseline with { ScannedValue = new string('s', 200) }).IsValid);
        Assert.True(validator.Validate(baseline with { PageSize = 1 }).IsValid);
        Assert.True(validator.Validate(baseline with { PageSize = 100 }).IsValid);
        Assert.True(validator.Validate(baseline with { PageIndex = int.MaxValue, PageSize = 1 }).IsValid);
        Assert.False(validator.Validate(baseline with { PageIndex = 0 }).IsValid);
        Assert.False(validator.Validate(baseline with { PageSize = 0 }).IsValid);
        Assert.False(validator.Validate(baseline with { PageSize = 101 }).IsValid);
        AssertOnlyError(validator, baseline with { PageIndex = int.MaxValue }, "");
    }

    private static void VerifyList<T>(AbstractValidator<T> validator, Func<string, string, int, int, T> request)
    {
        Assert.True(validator.Validate(request("org", "env", 0, 100)).IsValid);
        VerifyTenant(validator, (org, env) => request(org, env, 0, 100));
        AssertOnlyError(validator, request("org", "env", -1, 100), "Skip");
        Assert.True(validator.Validate(request("org", "env", int.MaxValue, 100)).IsValid);
        foreach (var take in new[] { 1, 500 })
            Assert.True(validator.Validate(request("org", "env", 0, take)).IsValid);
        foreach (var take in new[] { 0, 501 })
            AssertOnlyError(validator, request("org", "env", 0, take), "Take");
    }

    private static void VerifyTenant<T>(AbstractValidator<T> validator, Func<string, string, T> request)
    {
        Assert.True(validator.Validate(request("org", "env")).IsValid);
        Assert.True(validator.Validate(request(new string('o', 100), "env")).IsValid);
        Assert.True(validator.Validate(request("org", new string('e', 100))).IsValid);
        foreach (var tenant in new[] { null, "", "   ", new string('t', 101) })
        {
            AssertOnlyError(validator, request(tenant!, "env"), "OrganizationId");
            AssertOnlyError(validator, request("org", tenant!), "EnvironmentId");
        }
    }

    private static void AssertOnlyError<T>(AbstractValidator<T> validator, T request, string property)
    {
        var result = validator.Validate(request);
        Assert.False(result.IsValid);
        // FastEndpoints installs camelCase property naming when a TestServer starts.
        Assert.All(result.Errors, error => Assert.Equal(
            JsonNamingPolicy.CamelCase.ConvertName(property),
            JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName)));
    }
}
