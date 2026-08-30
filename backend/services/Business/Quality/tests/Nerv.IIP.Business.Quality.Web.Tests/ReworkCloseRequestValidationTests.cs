using FluentValidation;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class ReworkCloseRequestValidationTests
{
    [Fact]
    public void Close_request_rejects_client_supplied_rework_work_order_and_accepts_omission()
    {
        var validator = new CloseNonconformanceReportRequestValidator();

#pragma warning disable CS0618
        var forged = validator.Validate(new CloseNonconformanceReportRequest(
            new NonconformanceReportId(Guid.NewGuid()),
            "RW-FORGED",
            null,
            null,
            "close"));
        var omitted = validator.Validate(new CloseNonconformanceReportRequest(
            new NonconformanceReportId(Guid.NewGuid()),
            null,
            null,
            null,
            "close"));
#pragma warning restore CS0618

        Assert.False(forged.IsValid);
        Assert.Contains(forged.Errors, x => x.PropertyName == "ReworkWorkOrderId");
        Assert.True(omitted.IsValid);
    }
}
