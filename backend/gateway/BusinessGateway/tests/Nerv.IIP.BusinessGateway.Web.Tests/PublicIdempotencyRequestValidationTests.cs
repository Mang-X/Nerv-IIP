using FluentValidation;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Maintenance;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Mes;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Quality;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class PublicIdempotencyRequestValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Public_gateway_write_requests_reject_missing_or_blank_idempotency_keys(string? key)
    {
        Assert.False(new BusinessConsoleMesOperationTaskActionRequestValidator().Validate(
            new BusinessConsoleMesOperationTaskActionRequest(
                OperationTaskId: "OP-10",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                ReasonCode: null,
                IdempotencyKey: key!,
                ScopeKind: "organization",
                ScopeId: "org-001")).IsValid);

        Assert.False(new BusinessConsoleRecordProductionReportRequestValidator().Validate(
            new BusinessConsoleRecordProductionReportRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                WorkOrderId: "WO-001",
                OperationTaskId: "OP-10",
                GoodQuantity: 1m,
                ScrapQuantity: 0m,
                CompletesOperation: false,
                ReportedAtUtc: DateTimeOffset.UnixEpoch,
                IdempotencyKey: key!,
                ScopeKind: "organization",
                ScopeId: "org-001")).IsValid);

        Assert.False(new BusinessConsoleCreateInspectionRecordFromTaskRequestValidator().Validate(
            new BusinessConsoleCreateInspectionRecordFromTaskRequest(
                InspectionTaskId: "IT-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                ResultLines: [],
                DispositionReason: null,
                DispositionAttachmentFileIds: [],
                IdempotencyKey: key!)).IsValid);

        Assert.False(new BusinessConsoleCreateMaintenanceWorkOrderRequestValidator().Validate(
            new BusinessConsoleCreateMaintenanceWorkOrderRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                DeviceAssetId: "DEV-001",
                Priority: "high",
                SourceAlarmId: null,
                OpenedBy: "operator-001",
                IdempotencyKey: key!)).IsValid);

        Assert.False(new BusinessConsoleCompleteMaintenanceWorkOrderRequestValidator().Validate(
            new BusinessConsoleCompleteMaintenanceWorkOrderRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Result: "fixed",
                DowntimeReasonCode: "equipment-failure",
                DowntimeMinutes: 10,
                SpareParts: [],
                IdempotencyKey: key!)).IsValid);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "user-admin")]
    [InlineData("self", null)]
    [InlineData("   ", "   ")]
    public void Mes_task_actions_and_production_reports_require_an_explicit_nonblank_scope(
        string? scopeKind,
        string? scopeId)
    {
        Assert.False(new BusinessConsoleMesOperationTaskActionRequestValidator().Validate(
            new BusinessConsoleMesOperationTaskActionRequest(
                OperationTaskId: "OP-10",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                ReasonCode: null,
                IdempotencyKey: "operation-action-001",
                ScopeKind: scopeKind!,
                ScopeId: scopeId!)).IsValid);

        Assert.False(new BusinessConsoleRecordProductionReportRequestValidator().Validate(
            new BusinessConsoleRecordProductionReportRequest(
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                WorkOrderId: "WO-001",
                OperationTaskId: "OP-10",
                GoodQuantity: 1m,
                ScrapQuantity: 0m,
                CompletesOperation: false,
                ReportedAtUtc: DateTimeOffset.UnixEpoch,
                IdempotencyKey: "production-report-001",
                ScopeKind: scopeKind!,
                ScopeId: scopeId!)).IsValid);
    }
}
