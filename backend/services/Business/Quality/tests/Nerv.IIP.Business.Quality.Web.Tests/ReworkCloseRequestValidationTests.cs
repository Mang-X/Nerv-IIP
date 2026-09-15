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
        // 断 ErrorMessage 而不是 PropertyName：后者由 ValidatorOptions.Global.PropertyNameResolver 决定，
        // app.UseFastEndpoints(...) 启动时会把它换成 camelCase 并且不还原，于是断言随同程序集内的执行顺序
        // 时红时绿（#3342）。这条规则用 WithMessage 钉死了文案、模板不含 {PropertyName} 占位符，故不受解析器影响。
        Assert.Contains(
            forged.Errors,
            x => x.ErrorMessage == "ReworkWorkOrderId is bound only from the MES rework-work-order-created receipt.");
        Assert.True(omitted.IsValid);
    }
}
