using System.Net;
using System.Text;
using System.Text.Json;
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
            "WorkOrder",
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
            "WorkOrder",
            client => client.ReleaseWorkOrderAsync(
                "token",
                "WO-20260731-001",
                new BusinessConsoleMesReleaseWorkOrderRequest("WO-20260731-001", "org", "env", false, "idem-release"),
                CancellationToken.None));

    [Fact]
    public async Task Hold_work_order_returns_an_accepted_receipt_carrying_the_work_order_no() =>
        await AssertAcceptedReceiptAsync(
            "WO-20260731-001",
            "WorkOrder",
            client => client.HoldWorkOrderAsync(
                "token",
                "WO-20260731-001",
                new BusinessConsoleMesWorkOrderReasonRequest("WO-20260731-001", "org", "env", "设备待修", null),
                CancellationToken.None));

    [Fact]
    public async Task Cancel_work_order_returns_an_accepted_receipt_carrying_the_work_order_no() =>
        await AssertAcceptedReceiptAsync(
            "WO-20260731-001",
            "WorkOrder",
            client => client.CancelWorkOrderAsync(
                "token",
                "WO-20260731-001",
                new BusinessConsoleMesWorkOrderReasonRequest("WO-20260731-001", "org", "env", "订单取消", null),
                CancellationToken.None));

    [Fact]
    public async Task Force_release_quality_hold_returns_an_accepted_receipt_carrying_the_source_document() =>
        await AssertAcceptedReceiptAsync(
            "QH-000045",
            "QualityHold",
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
            "DispatchTask",
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
            "TelemetryProductionReportCandidate",
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
            "Defect",
            client => client.RecordDefectAsync(
                "token",
                new BusinessMesRecordDefectRequest(
                    "org", "env", "WO-20260731-001", "OP-000210", "SCRATCH", 2m,
                    DateTimeOffset.Parse("2026-08-25T14:30:00Z"), "idem-defect"),
                CancellationToken.None));

    [Fact]
    public async Task Record_defect_maps_the_public_contract_to_the_exact_mes_wire_payload()
    {
        string? capturedBody = null;
        var client = ClientReturning(AcceptedJson("DEF-000031"), capturedBody: body => capturedBody = body);
        var request = new BusinessMesRecordDefectRequest(
            "org", "env", "WO-20260731-001", "OP-000210", "SCRATCH", 2.5m,
            DateTimeOffset.Parse("2026-08-25T14:30:00Z"), "idem-defect");

        await client.RecordDefectAsync("token", request, CancellationToken.None);

        using var document = JsonDocument.Parse(capturedBody!);
        var body = document.RootElement;
        Assert.Equal(2.5m, body.GetProperty("quantity").GetDecimal());
        Assert.Equal("2026-08-25T14:30:00+00:00", body.GetProperty("recordedAtUtc").GetString());
        Assert.False(body.TryGetProperty("defectQuantity", out _));
        Assert.False(body.TryGetProperty("scopeKind", out _));
        Assert.False(body.TryGetProperty("scopeId", out _));
    }

    [Fact]
    public async Task Record_downtime_event_returns_an_accepted_receipt_carrying_the_downtime_no() =>
        await AssertAcceptedReceiptAsync(
            "DTE-000007",
            "DowntimeEvent",
            client => client.RecordDowntimeEventAsync(
                "token",
                new BusinessConsoleMesRecordDowntimeEventRequest(
                    "org", "env", "WO-20260731-001", "OP-000210", "DEV-1", "MECH-FAULT", new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero), "idem-downtime"),
                CancellationToken.None));

    [Fact]
    public async Task Record_downtime_v2_maps_real_context_and_recorded_time_to_the_exact_mes_wire_payload()
    {
        string? capturedBody = null;
        var capturedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var client = ClientReturning(
            AcceptedJson("DTE-000008"),
            capturedHeaders,
            body => capturedBody = body);
        var startedAtUtc = DateTimeOffset.Parse("2026-08-25T14:30:00Z");
        var request = new BusinessMesRecordDowntimeEventRequest(
            "org",
            "env",
            "WO-20260731-001",
            "OP-000210",
            "WC-CNC-01",
            "DEV-1",
            "MECH-FAULT",
            startedAtUtc,
            "idem-downtime-v2",
            DateTimeOffset.Parse("2026-08-25T15:00:00Z"));

        await client.RecordDowntimeEventAsync("token", request, CancellationToken.None);

        using var document = JsonDocument.Parse(capturedBody!);
        var body = document.RootElement;
        Assert.Equal("WC-CNC-01", body.GetProperty("workCenterId").GetString());
        Assert.Equal("MECH-FAULT", body.GetProperty("reasonCode").GetString());
        Assert.Equal("2026-08-25T14:30:00+00:00", body.GetProperty("startedAtUtc").GetString());
        Assert.Equal("2026-08-25T15:00:00+00:00", body.GetProperty("toUtc").GetString());
        Assert.Equal("idem-downtime-v2", body.GetProperty("idempotencyKey").GetString());
        Assert.False(body.TryGetProperty("reason", out _));
        Assert.False(body.TryGetProperty("fromUtc", out _));
        Assert.Equal("idem-downtime-v2", capturedHeaders["Idempotency-Key"]);
    }

    [Fact]
    public async Task Confirm_downtime_recovery_returns_an_accepted_receipt_carrying_the_downtime_no() =>
        await AssertAcceptedReceiptAsync(
            "DTE-000007",
            "DowntimeEvent",
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
            "ShiftHandover",
            client => client.CreateShiftHandoverAsync(
                "token",
                new BusinessConsoleMesCreateShiftHandoverForwardRequest(
                    "org", "env", "SHIFT-A", "TEAM-1", null, "idem-handover", null, "user-admin", "张三", null, null, null, null),
                CancellationToken.None));

    [Fact]
    public async Task Accept_shift_handover_returns_an_accepted_receipt_carrying_the_handover_no() =>
        await AssertAcceptedReceiptAsync(
            "SH-000012",
            "ShiftHandover",
            client => client.AcceptShiftHandoverAsync(
                "token",
                "SH-000012",
                new BusinessConsoleMesAcceptShiftHandoverForwardRequest("org", "env", "idem-accept", "user-admin", "李四"),
                CancellationToken.None));

    [Fact]
    public async Task A_receipt_without_a_reference_id_is_still_accepted_but_reports_no_document()
    {
        var client = ClientReturning("""{"data":{"status":"Accepted","referenceId":"","acceptedAtUtc":"2026-07-31T08:00:00Z"}}""");

        var response = await client.RecordDefectAsync(
            "token",
            new BusinessMesRecordDefectRequest(
                "org", "env", "WO-20260731-001", "OP-000210", "SCRATCH", 2m,
                DateTimeOffset.Parse("2026-08-25T14:30:00Z"), "idem-defect"),
            CancellationToken.None);

        Assert.True(response.Accepted);
        Assert.Equal("BusinessMes", response.DownstreamService);
        Assert.Equal("Defect", response.DownstreamDocumentType);
        Assert.Null(response.DownstreamDocumentId);
    }

    [Fact]
    public async Task Force_release_quality_hold_keeps_forwarding_the_governed_audit_headers()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var client = ClientReturning(AcceptedJson("QH-000045"), headers);

        var response = await client.ForceReleaseQualityHoldAsync(
            "token",
            "QH-000045",
            new BusinessConsoleMesForceReleaseQualityHoldRequest("QH-000045", "org", "env", "复检合格", "quality", null, "idem-hold"),
            "user:qa",
            "corr-governed",
            CancellationToken.None);

        // The receipt mapping must not swallow the governed headers the audit trail depends on.
        Assert.Equal("user:qa", headers["X-Authenticated-Actor"]);
        Assert.Equal("corr-governed", headers["X-Correlation-Id"]);
        Assert.Equal("idem-hold", headers["X-Idempotency-Key"]);
        Assert.True(response.Accepted);
        Assert.Equal("QH-000045", response.DownstreamDocumentId);
    }

    [Fact]
    public async Task Assign_dispatch_task_keeps_forwarding_the_authenticated_actor_header()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? requestJson = null;
        var client = ClientReturning(AcceptedJson("OP-000210"), headers, body => requestJson = body);

        var response = await client.AssignDispatchTaskAsync(
            "token",
            "OP-000210",
            new BusinessConsoleMesAssignDispatchTaskForwardRequest(
                "org",
                "env",
                "user-1",
                "张三",
                null,
                "SHIFT-A",
                "idem-assign",
                Participants:
                [
                    new BusinessConsoleMesDispatchParticipantForwardInput("user-1", "张三", 60m),
                    new BusinessConsoleMesDispatchParticipantForwardInput("user-2", "李四", 40m),
                ]),
            "user:planner",
            CancellationToken.None);

        Assert.Equal("user:planner", headers["X-Authenticated-Actor"]);
        using var requestDocument = JsonDocument.Parse(Assert.IsType<string>(requestJson));
        var participants = requestDocument.RootElement.GetProperty("participants");
        Assert.Equal(2, participants.GetArrayLength());
        Assert.Equal("user-1", participants[0].GetProperty("workerId").GetString());
        Assert.Equal("张三", participants[0].GetProperty("workerName").GetString());
        Assert.Equal(60m, participants[0].GetProperty("sharePercent").GetDecimal());
        Assert.True(response.Accepted);
        Assert.Equal("OP-000210", response.DownstreamDocumentId);
    }

    private static async Task AssertAcceptedReceiptAsync(
        string referenceId,
        string expectedDocumentType,
        Func<HttpBusinessMesClient, Task<BusinessConsoleAcceptedResponse>> call,
        string status = "Accepted")
    {
        var client = ClientReturning(AcceptedJson(referenceId, status));

        var response = await call(client);

        Assert.True(response.Accepted);
        Assert.Equal("BusinessMes", response.DownstreamService);
        Assert.Equal(expectedDocumentType, response.DownstreamDocumentType);
        Assert.Equal(referenceId, response.DownstreamDocumentId);
    }

    private static string AcceptedJson(string referenceId, string status = "Accepted") =>
        "{\"data\":{\"status\":\"" + status + "\",\"referenceId\":\"" + referenceId + "\",\"acceptedAtUtc\":\"2026-07-31T08:00:00Z\"}}";

    private static HttpBusinessMesClient ClientReturning(
        string json,
        IDictionary<string, string>? capturedHeaders = null,
        Action<string>? capturedBody = null) =>
        new(new HttpClient(new StubHandler(json, capturedHeaders, capturedBody)) { BaseAddress = new Uri("http://mes") });

    private sealed class StubHandler(
        string json,
        IDictionary<string, string>? capturedHeaders,
        Action<string>? capturedBody) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (capturedHeaders is not null)
            {
                foreach (var header in request.Headers)
                {
                    capturedHeaders[header.Key] = string.Join(',', header.Value);
                }
            }

            if (capturedBody is not null && request.Content is not null)
            {
                capturedBody(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
