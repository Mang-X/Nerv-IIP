using System.Linq;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;
using Nerv.IIP.Contracts.Coding;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleCodeRuleVersionReasonValidationTests
{
    public static TheoryData<string?> InvalidReasons => new()
    {
        null,
        "",
        " ",
        "\t",
        "　",
        new string('变', 501),
        $" {new string('变', 500)}",
        $"{new string('变', 500)} ",
    };

    [Theory]
    [MemberData(nameof(InvalidReasons))]
    public void Code_rule_version_rejects_invalid_change_reason_differentially(string? reason)
    {
        var validator = new BusinessConsoleCreateCodeRuleVersionRequestValidator();

        Assert.True(validator.Validate(Request("调整编码规范")).IsValid);
        Assert.False(validator.Validate(Request(reason)).IsValid);
    }

    [Fact]
    public void Code_rule_version_accepts_the_500_character_boundary()
    {
        var result = new BusinessConsoleCreateCodeRuleVersionRequestValidator()
            .Validate(Request(new string('变', 500)));

        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(failure => failure.ErrorMessage)));
    }

    private static BusinessConsoleCreateCodeRuleVersionRequest Request(string? reason) =>
        new(
            "org-001",
            "env-dev",
            "master-data.sku",
            "SKU 编码规则",
            "sku",
            ScopeDimension.Organization,
            [CodeRuleSegment.ConstantOf("SKU-"), CodeRuleSegment.SequenceOf(4)],
            true,
            new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero),
            "admin-001",
            reason!);
}
