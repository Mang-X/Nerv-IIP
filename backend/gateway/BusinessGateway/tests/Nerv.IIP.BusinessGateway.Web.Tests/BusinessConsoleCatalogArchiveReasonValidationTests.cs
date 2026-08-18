using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleCatalogArchiveReasonValidationTests
{
    public static TheoryData<string> InvalidReasons => new()
    {
        "",
        " ",
        "\t",
        "　",
        new string('停', 501),
    };

    [Theory]
    [MemberData(nameof(InvalidReasons))]
    public void Product_category_archive_rejects_invalid_reason_differentially(string reason)
    {
        var validator = new BusinessConsoleArchiveProductCategoryRequestValidator();

        Assert.True(validator.Validate(ProductCategoryRequest("产品线调整")).IsValid);
        Assert.False(validator.Validate(ProductCategoryRequest(reason)).IsValid);
    }

    [Fact]
    public void Product_category_archive_accepts_the_500_character_boundary()
    {
        var result = new BusinessConsoleArchiveProductCategoryRequestValidator()
            .Validate(ProductCategoryRequest(new string('停', 500)));

        Assert.True(result.IsValid);
    }

    [Theory]
    [MemberData(nameof(InvalidReasons))]
    public void Skill_archive_rejects_invalid_reason_differentially(string reason)
    {
        var validator = new BusinessConsoleArchiveSkillRequestValidator();

        Assert.True(validator.Validate(SkillRequest("资质目录调整")).IsValid);
        Assert.False(validator.Validate(SkillRequest(reason)).IsValid);
    }

    [Fact]
    public void Skill_archive_accepts_the_500_character_boundary()
    {
        var result = new BusinessConsoleArchiveSkillRequestValidator()
            .Validate(SkillRequest(new string('停', 500)));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(typeof(BusinessConsoleArchiveProductCategoryRequest))]
    [InlineData(typeof(BusinessConsoleArchiveSkillRequest))]
    public void Archive_request_reason_has_no_default_value(Type requestType)
    {
        var reason = Assert.Single(
            Assert.Single(requestType.GetConstructors()).GetParameters(),
            parameter => parameter.Name == "Reason");

        Assert.False(reason.IsOptional);
        Assert.False(reason.HasDefaultValue);
    }

    private static BusinessConsoleArchiveProductCategoryRequest ProductCategoryRequest(string reason) =>
        new("CAT-FG", "org-001", "env-dev", reason);

    private static BusinessConsoleArchiveSkillRequest SkillRequest(string reason) =>
        new("SK-WELD", "org-001", "env-dev", reason);
}
