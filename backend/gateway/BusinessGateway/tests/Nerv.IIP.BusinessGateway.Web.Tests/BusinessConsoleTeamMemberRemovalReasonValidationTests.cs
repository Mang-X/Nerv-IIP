using System.Linq;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleTeamMemberRemovalReasonValidationTests
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
    public void Team_member_removal_rejects_invalid_reason_differentially(string reason)
    {
        var validator = new BusinessConsoleRemoveTeamMemberRequestValidator();

        Assert.True(validator.Validate(Request("人员调离")).IsValid);
        Assert.False(validator.Validate(Request(reason)).IsValid);
    }

    [Fact]
    public void Team_member_removal_accepts_the_500_character_boundary()
    {
        var result = new BusinessConsoleRemoveTeamMemberRequestValidator()
            .Validate(Request(new string('停', 500)));

        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(failure => failure.ErrorMessage)));
    }

    [Fact]
    public void Team_member_removal_request_reason_has_no_default_value()
    {
        var reason = Assert.Single(
            Assert.Single(typeof(BusinessConsoleRemoveTeamMemberRequest).GetConstructors()).GetParameters(),
            parameter => parameter.Name == "Reason");

        Assert.False(reason.IsOptional);
        Assert.False(reason.HasDefaultValue);
    }

    private static BusinessConsoleRemoveTeamMemberRequest Request(string reason) =>
        new("org-001", "env-dev", "T-001", "user-001", reason);
}
