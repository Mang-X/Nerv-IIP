using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Quality;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessConsoleNcrCloseValidationTests
{
    [Fact]
    public void Close_facade_rejects_client_supplied_rework_work_order_and_accepts_omission()
    {
        var validator = new BusinessConsoleNcrCloseRequestValidator();

#pragma warning disable CS0618
        var forged = validator.Validate(new BusinessConsoleNcrCloseRequest(
            "ncr-001",
            "org-001",
            "env-dev",
            "RW-FORGED",
            null,
            null,
            "close"));
        var omitted = validator.Validate(new BusinessConsoleNcrCloseRequest(
            "ncr-001",
            "org-001",
            "env-dev",
            null,
            null,
            null,
            "close"));
#pragma warning restore CS0618

        Assert.False(forged.IsValid);
        Assert.Contains(
            forged.Errors,
            x => x.ErrorMessage == "ReworkWorkOrderId is bound only from the MES rework-work-order-created receipt.");
        Assert.True(omitted.IsValid);
    }
}
