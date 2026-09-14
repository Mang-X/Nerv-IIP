using System.Text.Json;
using FluentValidation;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class PublicIdempotencyRequestValidationTests
{
    [Fact]
    public void Start_changeover_rejects_a_missing_tooling_check_result()
    {
        var request = JsonSerializer.Deserialize<StartChangeoverRequest>("""
            {
              "organizationId": "org-001",
              "environmentId": "env-dev",
              "workCenterId": "WC-01",
              "deviceAssetId": "DEV-01",
              "operatorId": "operator-01",
              "startedAtUtc": "2026-09-05T01:00:00Z",
              "idempotencyKey": "changeover-001"
            }
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.False(new StartChangeoverRequestValidator().Validate(request).IsValid);
    }

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_report_rejects_blank_intent_fingerprint_when_provided(string fingerprint)
    {
        var request = ValidProductionReportRequest() with { ReportIntentFingerprint = fingerprint };

        Assert.False(new RecordProductionReportRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Production_report_accepts_exact_opaque_intent_fingerprint_up_to_256_characters()
    {
        var fingerprint = $"  {new string('x', 252)}  ";
        var request = ValidProductionReportRequest() with { ReportIntentFingerprint = fingerprint };

        Assert.True(new RecordProductionReportRequestValidator().Validate(request).IsValid);
        Assert.Equal(fingerprint, request.ReportIntentFingerprint);
        Assert.False(new RecordProductionReportRequestValidator().Validate(
            request with { ReportIntentFingerprint = fingerprint + "x" }).IsValid);
    }

    private static RecordProductionReportRequest ValidProductionReportRequest() =>
        new(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            1m,
            0m,
            false,
            DateTimeOffset.UnixEpoch,
            "intent-validation-001");
}
