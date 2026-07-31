using System.Net;
using System.Text;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #1341: every MES write face answers <c>{ status, referenceId, acceptedAtUtc }</c>. Deserializing that body
/// straight into the console contract left <c>accepted</c> false and dropped the downstream document number,
/// so the console reported a real acceptance as a non-acceptance. Each proxy must map the receipt explicitly.
/// </summary>
public sealed class BusinessMesAcceptedReceiptClientTests
{
    [Fact]
    public async Task Convert_plan_to_work_order_returns_an_accepted_receipt_carrying_the_work_order_no() =>
        await AssertAcceptedReceiptAsync(
            "WO-20260731-001",
            "mes-work-order",
            client => client.ConvertPlanToWorkOrderAsync(
                "token",
                "PLAN-001",
                new BusinessConsoleMesConvertPlanToWorkOrderRequest(
                    "PLAN-001", "org", "env", null, "SKU-1", null, 10m, "EA", null, null),
                CancellationToken.None));

    [Fact]
    public async Task Release_work_order_returns_an_accepted_receipt_carrying_the_work_order_no() =>
        await AssertAcceptedReceiptAsync(
            "WO-20260731-001",
            "mes-work-order",
            client => client.ReleaseWorkOrderAsync(
                "token",
                "WO-20260731-001",
                new BusinessConsoleMesReleaseWorkOrderRequest("WO-20260731-001", "org", "env", false, "idem-release"),
                CancellationToken.None));

    [Fact]
    public async Task Hold_work_order_returns_an_accepted_receipt_carrying_the_work_order_no() =>
        await AssertAcceptedReceiptAsync(
            "WO-20260731-001",
            "mes-work-order",
            client => client.HoldWorkOrderAsync(
                "token",
                "WO-20260731-001",
                new BusinessConsoleMesWorkOrderReasonRequest("WO-20260731-001", "org", "env", "设备待修", null),
                CancellationToken.None));

    [Fact]
    public async Task Cancel_work_order_returns_an_accepted_receipt_carrying_the_work_order_no() =>
        await AssertAcceptedReceiptAsync(
            "WO-20260731-001",
            "mes-work-order",
            client => client.CancelWorkOrderAsync(
                "token",
                "WO-20260731-001",
                new BusinessConsoleMesWorkOrderReasonRequest("WO-20260731-001", "org", "env", "订单取消", null),
                CancellationToken.None));

    [Fact]
    public async Task Force_release_quality_hold_returns_an_accepted_receipt_carrying_the_source_document() =>
        await AssertAcceptedReceiptAsync(
            "QH-000045",
            "mes-quality-hold",
            client => client.ForceReleaseQualityHoldAsync(
                "token",
                "QH-000045",
                new BusinessConsoleMesForceReleaseQualityHoldRequest("QH-000045", "org", "env", "复检合格", "quality", null, "idem-hold"),
                "user:qa",
                "corr-1",
                CancellationToken.None));

    [Fact]
    public async Task Assign_dispatch_task_returns_an_accepted_receipt_carrying_the_operation_task() =>
        await AssertAcceptedReceiptAsync(
            "OP-000210",
            "mes-dispatch-task",
            client => client.AssignDispatchTaskAsync(
                "token",
                "OP-000210",
                new BusinessConsoleMesAssignDispatchTaskForwardRequest("org", "env", "user-1", "张三", null, "SHIFT-A", "idem-assign"),
                "user:planner",
                CancellationToken.None));

    [Fact]
    public async Task Dismiss_telemetry_candidate_returns_an_accepted_receipt_carrying_the_candidate() =>
        await AssertAcceptedReceiptAsync(
            "019f0000-0000-7000-8000-000000000001",
            "mes-telemetry-production-report-candidate",
            client => client.DismissTelemetryCandidateAsync(
                "token",
                "019f0000-0000-7000-8000-000000000001",
                new BusinessConsoleMesTelemetryCandidateDismissRequest("019f0000-0000-7000-8000-000000000001", "org", "env", "重复采集"),
                "user:operator",
                CancellationToken.None),
            // The dismiss endpoint answers status "dismissed"; a 2xx body is still an acceptance.
            status: "dismissed");

    [Fact]
    public async Task Record_defect_returns_an_accepted_receipt_carrying_the_defect_no() =>
        await AssertAcceptedReceiptAsync(
            "DEF-000031",
            "mes-defect",
            client => client.RecordDefectAsync(
                "token",
                new BusinessConsoleMesRecordDefectRequest("org", "env", "WO-20260731-001", "OP-000210", "SCRATCH", 2m, null, null, "idem-defect"),
                CancellationToken.None));

    [Fact]
    public async Task Record_downtime_event_returns_an_accepted_receipt_carrying_the_downtime_no() =>
        await AssertAcceptedReceiptAsync(
            "DTE-000007",
            "mes-downtime-event",
            client => client.RecordDowntimeEventAsync(
                "token",
                new BusinessConsoleMesRecordDowntimeEventRequest(
                    "org", "env", "WO-20260731-001", "OP-000210", "DEV-1", "MECH-FAULT", new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero), "idem-downtime"),
                CancellationToken.None));

    [Fact]
    public async Task Confirm_downtime_recovery_returns_an_accepted_receipt_carrying_the_downtime_no() =>
        await AssertAcceptedReceiptAsync(
            "DTE-000007",
            "mes-downtime-event",
            client => client.ConfirmDowntimeRecoveryAsync(
                "token",
                "DTE-000007",
                new BusinessConsoleMesRecoverDowntimeEventRequest(
                    "DTE-000007", "org", "env", new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero), "idem-recover"),
                CancellationToken.None));

    [Fact]
    public async Task Create_shift_handover_returns_an_accepted_receipt_carrying_the_handover_no() =>
        await AssertAcceptedReceiptAsync(
            "SH-000012",
            "mes-shift-handover",
            client => client.CreateShiftHandoverAsync(
                "token",
                new BusinessConsoleMesCreateShiftHandoverRequest("org", "env", "SHIFT-A", "TEAM-1", null, "idem-handover"),
                CancellationToken.None));

    [Fact]
    public async Task Accept_shift_handover_returns_an_accepted_receipt_carrying_the_handover_no() =>
        await AssertAcceptedReceiptAsync(
            "SH-000012",
            "mes-shift-handover",
            client => client.AcceptShiftHandoverAsync(
                "token",
                "SH-000012",
                new BusinessConsoleMesAcceptShiftHandoverRequest("SH-000012", "org", "env", "idem-accept"),
                CancellationToken.None));

    [Fact]
    public async Task A_receipt_without_a_reference_id_is_still_accepted_but_reports_no_document()
    {
        var client = ClientReturning("""{"data":{"status":"Accepted","referenceId":"","acceptedAtUtc":"2026-07-31T08:00:00Z"}}""");

        var response = await client.RecordDefectAsync(
            "token",
            new BusinessConsoleMesRecordDefectRequest("org", "env", "WO-20260731-001", "OP-000210", "SCRATCH", 2m, null, null, "idem-defect"),
            CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("business-mes", response.DownstreamService);
        Assert.Equal("mes-defect", response.DownstreamDocumentType);
        Assert.Null(response.DownstreamDocumentId);
    }

    private static async Task AssertAcceptedReceiptAsync(
        string referenceId,
        string expectedDocumentType,
        Func<HttpBusinessMesClient, Task<BusinessConsoleAcceptedResponse>> call,
        string status = "Accepted")
    {
        var client = ClientReturning(
            "{\"data\":{\"status\":\"" + status + "\",\"referenceId\":\"" + referenceId + "\",\"acceptedAtUtc\":\"2026-07-31T08:00:00Z\"}}");

        var response = await call(client);

        Assert.True(response.Accepted);
        Assert.Equal("business-mes", response.DownstreamService);
        Assert.Equal(expectedDocumentType, response.DownstreamDocumentType);
        Assert.Equal(referenceId, response.DownstreamDocumentId);
    }

    private static HttpBusinessMesClient ClientReturning(string json) =>
        new(new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("http://mes") });

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}
