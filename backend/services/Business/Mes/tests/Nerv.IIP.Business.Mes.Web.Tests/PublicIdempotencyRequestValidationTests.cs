using FluentValidation;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class PublicIdempotencyRequestValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Public_mes_write_requests_reject_missing_or_blank_idempotency_keys(string? key)
    {
        Assert.False(new OperationTaskActionRequestValidator().Validate(
            new OperationTaskActionRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                OperationTaskId: "OP-10",
                ChangedAtUtc: null,
                IdempotencyKey: key!)).IsValid);

        Assert.False(new RecordProductionReportRequestValidator().Validate(
            new RecordProductionReportRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                WorkOrderId: "WO-001",
                OperationTaskId: "OP-10",
                GoodQuantity: 1m,
                ScrapQuantity: 0m,
                CompletesOperation: false,
                ReportedAtUtc: DateTimeOffset.UnixEpoch,
                IdempotencyKey: key!)).IsValid);
    }
}
