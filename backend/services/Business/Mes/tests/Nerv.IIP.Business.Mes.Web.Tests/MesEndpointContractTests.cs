using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Auth;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MesEndpointContractTests
{
    // Contract: HttpApi + Regression. Authority: Issue #2223 acceptance 3.
    [Fact]
    public async Task Material_readiness_endpoint_exposes_the_frozen_substitute_candidate_ids()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new MaterialReadinessSender());
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.GetAsync(
            "/api/business/v1/mes/work-orders/WO-SUB-HTTP/material-readiness?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = body.RootElement.GetProperty("items")[0];
        Assert.Equal(
            ["MAT-ALT-A", "MAT-ALT-B"],
            row.GetProperty("substituteMaterialIds").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    [Fact]
    public async Task Material_scan_prevalidation_endpoint_is_exposed_for_strong_mes_identifiers()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/material-scan-prevalidation",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                materialIssueRequestId = "MIR-001",
                workOrderId = "WO-001",
                operationTaskId = "OP-10",
            });

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Context_scan_prevalidation_endpoint_is_exposed_for_resolved_strong_identifiers()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/context-scan-prevalidation",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                workOrderId = "WO-001",
                operationTaskId = "OP-10",
                objectType = "deviceAsset",
                scannedObjectId = "device-001",
            });

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Context_scan_http_distinguishes_personnel_mismatch_from_qualification_source_unavailable()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender, DistinguishingContextScanSender>();
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var mismatch = await client.PostAsJsonAsync(
            "/api/business/v1/mes/context-scan-prevalidation",
            ContextScanRequest("worker-other"));
        var sourceUnavailable = await client.PostAsJsonAsync(
            "/api/business/v1/mes/context-scan-prevalidation",
            ContextScanRequest("worker-001"));

        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        using var mismatchBody = JsonDocument.Parse(await mismatch.Content.ReadAsStringAsync());
        var mismatchData = mismatchBody.RootElement.TryGetProperty("data", out var mismatchPayload)
            ? mismatchPayload
            : mismatchBody.RootElement;
        Assert.Equal(
            "personnel-mismatch",
            mismatchData.GetProperty("reasonCode").GetString());
        Assert.Equal(HttpStatusCode.OK, sourceUnavailable.StatusCode);
        var sourceUnavailableBody = await sourceUnavailable.Content.ReadAsStringAsync();
        Assert.Contains("WORKER_SKILL_SOURCE_UNAVAILABLE", sourceUnavailableBody, StringComparison.Ordinal);
        Assert.DoesNotContain("personnel-mismatch", sourceUnavailableBody, StringComparison.Ordinal);
    }

    private static object ContextScanRequest(string scannedObjectId) =>
        new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-001",
            operationTaskId = "OP-10",
            objectType = "personnel",
            scannedObjectId,
        };

    [Fact]
    public async Task Material_scan_prevalidation_endpoint_preserves_accepted_handler_facts_on_the_http_wire()
    {
        var sender = new AcceptedMaterialScanSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/material-scan-prevalidation",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                materialIssueRequestId = "MIR-001",
                workOrderId = "WO-001",
                operationTaskId = "OP-10",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.TryGetProperty("data", out var payload)
            ? payload
            : document.RootElement;
        Assert.Equal("accepted", data.GetProperty("decision").GetString());
        Assert.Equal("material-scan-accepted", data.GetProperty("reasonCode").GetString());
        Assert.Equal("MIR-001", data.GetProperty("materialIssueRequestId").GetString());
        Assert.Equal("WO-001", data.GetProperty("workOrderId").GetString());
        Assert.Equal("OP-10", data.GetProperty("operationTaskId").GetString());
        Assert.Equal("MAT-SUB", data.GetProperty("materialId").GetString());
        Assert.Equal("LOT-001", data.GetProperty("materialLotId").GetString());
        Assert.Equal("substitute", data.GetProperty("materialQualification").GetString());
    }

    [Fact]
    public async Task Record_downtime_rejects_missing_real_context_before_sending_command()
    {
        var sender = new CapturingRecordDowntimeSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/downtime-events",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                workOrderId = "WO-001",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public async Task Record_downtime_endpoint_preserves_work_center_reason_time_and_idempotency_in_the_command()
    {
        var sender = new CapturingRecordDowntimeSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        var startedAtUtc = DateTimeOffset.Parse("2026-08-25T14:30:00Z");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/downtime-events",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                workOrderId = "WO-001",
                operationTaskId = "OP-10",
                workCenterId = "WC-CNC-01",
                deviceAssetId = "DEV-01",
                reasonCode = "MECH-FAULT",
                startedAtUtc,
                idempotencyKey = "downtime-http-001",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sender.CallCount);
        Assert.NotNull(sender.LastCommand);
        var command = sender.LastCommand!;
        Assert.Equal("WC-CNC-01", command.WorkCenterId);
        Assert.Equal("MECH-FAULT", command.Reason);
        Assert.Equal(startedAtUtc, command.FromUtc);
        Assert.Equal("downtime-http-001", command.IdempotencyKey);
    }

    [Fact]
    public void Production_report_internal_compatibility_constructor_cannot_mint_a_caller_intent_receipt()
    {
        var internalCommand = new RecordProductionReportCommand(
            "org-001", "env-dev", "WO-001", "OP-10", 1m, 0m, false, DateTimeOffset.UnixEpoch);
        var httpCommand = new RecordProductionReportCommand(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            1m,
            0m,
            false,
            DateTimeOffset.UnixEpoch,
            "caller-intent-001");
        var property = typeof(RecordProductionReportCommand).GetProperty(
            "PersistsCallerIntentReceipt",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(property);
        Assert.False(Assert.IsType<bool>(property.GetValue(internalCommand)));
        Assert.True(Assert.IsType<bool>(property.GetValue(httpCommand)));
    }

    [Fact]
    public async Task Lifecycle_conflict_endpoint_returns_409_with_safe_code()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new LifecycleConflictSender());
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-STATE/pause",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                changedAtUtc = "2026-07-27T10:00:00Z",
                idempotencyKey = "pause-lifecycle-conflict",
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"message\":\"lifecycle-conflict\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Queued", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authorize_start_endpoint_forwards_scoped_request_and_ignores_caller_supplied_audit_fields()
    {
        var sender = new CapturingAuthorizeStartSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "principal:forged-header");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-authorize-start-http");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-authorize-start-http");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-HTTP/authorize-start",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                approvalChainId = " approval-1960-http ",
                reason = "  HTTP route evidence  ",
                authorizedBy = "principal:forged",
                changedAtUtc = "2000-01-01T00:00:00Z",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<AuthorizeAndStartOperationTaskCommand>(sender.Command);
        Assert.Equal("org-001", command.OrganizationId);
        Assert.Equal("env-dev", command.EnvironmentId);
        Assert.Equal("OP-HTTP", command.OperationTaskId);
        Assert.Equal(" approval-1960-http ", command.ApprovalChainId);
        Assert.Equal("  HTTP route evidence  ", command.Reason);
        Assert.Equal("corr-authorize-start-http", command.CorrelationId);
        Assert.Equal("idem-authorize-start-http", command.IdempotencyKey);
    }

    [Fact]
    public async Task Line_side_return_endpoint_declares_and_returns_409_for_a_domain_conflict()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new LifecycleConflictSender());
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "return-contract-conflict");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/material-issue-requests/MIR-CONFLICT/line-side-returns",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                returnedQuantity = 1m,
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("\"success\":false", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Complete_operation_endpoint_preserves_readable_predecessor_sequences_without_raw_task_ids()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new PreviousOperationIncompleteSender());
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-CURRENT-INTERNAL/complete",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                idempotencyKey = "complete-predecessor-rejected",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("前序工序尚未完成：工序 10、工序 20 等 4 道。", body, StringComparison.Ordinal);
        Assert.DoesNotContain("OP-PREVIOUS-INTERNAL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("OP-CURRENT-INTERNAL", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_plan_endpoint_returns_422_with_routing_snapshot_error_code()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new RoutingSnapshotMissingSender());
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/production-plans/SUG-001/work-orders",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                skuId = "FG-QJ-S1-R",
                productionVersionId = (string?)null,
                plannedQuantity = 12m,
                uomCode = "PCS",
                dueUtc = "2026-07-23T08:00:00Z",
                requestedAtUtc = "2026-07-21T08:00:00Z",
                idempotencyKey = "routing-snapshot-http-422",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"message\":\"ROUTING_SNAPSHOT_MISSING\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operation_action_endpoint_replays_same_key_across_server_generated_timestamps()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-HTTP-REPLAY",
            "SKU-FG",
            "PV-001",
            10m,
            1,
            DateTimeOffset.Parse("2026-07-29T08:00:00Z"),
            "PCS");
        workOrder.MarkReleased();
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            DateTimeOffset.Parse("2026-07-28T08:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-HTTP-REPLAY",
            "OP-HTTP-REPLAY",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            DateTimeOffset.Parse("2026-07-28T08:00:00Z"),
            TimeSpan.FromHours(1),
            null,
            null));
        await dbContext.SaveChangesAsync();
        var sender = new RealOperationActionSender(
            new ChangeOperationTaskStateCommandHandler(dbContext));

        // The endpoint stamps the command with the injected TimeProvider when the caller omits ChangedAtUtc.
        // Replacing it with a fake clock makes "the two requests carry different server timestamps" a fact the
        // test controls, instead of a wall-clock gap that only probably produces two distinct instants. The
        // anchor is the real now because the MES readiness path still evaluates the seeded work order against
        // real-world dates; only the delta between the two requests is fabricated.
        var serverClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(serverClock);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        var body = new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            idempotencyKey = "mes-http-replay-001",
        };

        var first = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-HTTP-REPLAY/start",
            body);
        serverClock.Advance(TimeSpan.FromMinutes(1));
        var replay = await client.PostAsJsonAsync(
            "/api/business/v1/mes/operation-tasks/OP-HTTP-REPLAY/start",
            body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await replay.Content.ReadAsStringAsync());
        Assert.Equal(2, sender.CallCount);
        // The point of the test: the two commands really did carry different server-generated timestamps.
        Assert.Equal(2, sender.ObservedChangedAtUtc.Count);
        Assert.NotEqual(sender.ObservedChangedAtUtc[0], sender.ObservedChangedAtUtc[1]);
    }

    [Fact]
    public async Task Record_defect_endpoint_preserves_the_caller_recorded_time_in_the_command()
    {
        var sender = new CapturingRecordDefectSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        var recordedAtUtc = DateTimeOffset.Parse("2026-08-25T14:30:00Z");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/defects", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-QUALITY",
            defectCode = "SCRATCH",
            quantity = 2.5m,
            recordedAtUtc,
            idempotencyKey = "defect-recorded-time-001",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sender.CallCount);
        Assert.NotNull(sender.LastCommand);
        Assert.Equal(recordedAtUtc, sender.LastCommand.RecordedAtUtc);
        Assert.Null(sender.LastCommand.OperationTaskId);
    }

    [Fact]
    public async Task Record_defect_endpoint_rejects_a_missing_recorded_time_before_dispatch()
    {
        var sender = new CapturingRecordDefectSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/defects", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-QUALITY",
            operationTaskId = "OP-10",
            defectCode = "SCRATCH",
            quantity = 2.5m,
            idempotencyKey = "defect-missing-time-001",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public async Task Record_production_report_endpoint_returns_strong_id_wire_shape()
    {
        var productionReportId = Guid.Parse("019f855b-5cb0-7550-a509-d2ee7b021689");
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new ProductionReportWireShapeSender(productionReportId));
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/production-reports", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-WIRE-001",
            operationTaskId = "OP-WIRE-10",
            goodQuantity = 1m,
            scrapQuantity = 0m,
            completesOperation = false,
            reportedAtUtc = "2026-07-21T15:46:24Z",
            idempotencyKey = "wire-shape-001",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;
        var wireId = root.GetProperty("productionReportId");
        Assert.Equal(JsonValueKind.Object, wireId.ValueKind);
        Assert.True(wireId.TryGetProperty("id", out var id), rawBody);
        Assert.Equal(productionReportId, id.GetGuid());
        Assert.Equal("PRPT-WIRE-001", root.GetProperty("reportNo").GetString());
    }

    // 验收 #1948/#2694：MES 写面端点必须把网关注入的报工人和调用方幂等键原样转交给命令。
    [Fact]
    public async Task Record_production_report_endpoint_forwards_the_injected_operator_and_idempotency_key_to_the_command()
    {
        var sender = new ProductionReportWireShapeSender(Guid.Parse("019f855b-5cb0-7550-a509-d2ee7b021689"));
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/production-reports", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-WIRE-001",
            operationTaskId = "OP-WIRE-10",
            goodQuantity = 1m,
            scrapQuantity = 0m,
            completesOperation = false,
            reportedAtUtc = "2026-07-21T15:46:24Z",
            idempotencyKey = "wire-operator-001",
            reportedBy = "user-emp-010",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user-emp-010", sender.Command?.ReportedBy);
        Assert.Equal("wire-operator-001", sender.Command?.IdempotencyKey);
    }

    // 幂等键是记录报工写面的硬前置：RecordProductionReportRequestValidator 先把缺失的幂等键拒成 400，命令不会发出。
    [Fact]
    public async Task Record_production_report_endpoint_rejects_a_missing_idempotency_key()
    {
        var sender = new ProductionReportWireShapeSender(Guid.Parse("019f855b-5cb0-7550-a509-d2ee7b021689"));
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/production-reports", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-WIRE-001",
            operationTaskId = "OP-WIRE-10",
            goodQuantity = 1m,
            scrapQuantity = 0m,
            completesOperation = false,
            reportedAtUtc = "2026-07-21T15:46:24Z",
            reportedBy = "user-emp-010",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(sender.Command);
    }

    [Fact]
    public async Task Create_finished_goods_receipt_endpoint_returns_strong_id_wire_shape()
    {
        var receiptRequestId = Guid.Parse("019f88b9-1d59-7cb3-b4a0-37b88e78422e");
        var sender = new FinishedGoodsReceiptWireShapeSender(receiptRequestId);
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/finished-goods-receipt-requests", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            workOrderId = "WO-WIRE-001",
            skuId = "SKU-FG-WIRE-001",
            quantity = 1m,
            uomCode = "PCS",
            requestedAtUtc = "2026-07-22T07:00:00Z",
            unitCost = 99.99m,
            idempotencyKey = "wire-shape-001",
            producedLotNo = "LOT-FG-WIRE-001",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(rawBody);
        var root = body.RootElement;
        var wireId = root.GetProperty("finishedGoodsReceiptRequestId");
        Assert.Equal(JsonValueKind.Object, wireId.ValueKind);
        Assert.True(wireId.TryGetProperty("id", out var id), rawBody);
        Assert.Equal(receiptRequestId, id.GetGuid());
        Assert.Equal("FGR-WIRE-001", root.GetProperty("requestNo").GetString());
        Assert.NotNull(sender.Command);
        Assert.Null(sender.Command!.UnitCost);
    }

    [Fact]
    public void Force_release_request_uses_authenticated_principal_and_governed_headers()
    {
        Assert.Null(typeof(ForceReleaseQualityHoldRequest).GetProperty("Actor"));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "supervisor-007")],
                "test")),
        };
        context.Request.Headers["X-Correlation-Id"] = "corr-force-007";
        context.Request.Headers["X-Idempotency-Key"] = "idem-force-007";

        var governed = MesQualityHoldRequestContext.Resolve(context);

        Assert.Equal("user:supervisor-007", governed.Actor);
        Assert.Equal("corr-force-007", governed.CorrelationId);
        Assert.Equal("idem-force-007", governed.IdempotencyKey);
    }

    [Fact]
    public void Force_release_internal_service_accepts_canonical_forwarded_actor()
    {
        var context = CreateQualityHoldContext(
            [
                new Claim(ClaimTypes.NameIdentifier, "internal-service"),
                new Claim("token_type", "internal_service"),
            ],
            "user:supervisor-008");

        var governed = MesQualityHoldRequestContext.Resolve(context);

        Assert.Equal("user:supervisor-008", governed.Actor);
    }

    [Fact]
    public void Force_release_user_ignores_forged_forwarded_actor()
    {
        var context = CreateQualityHoldContext(
            [new Claim(ClaimTypes.NameIdentifier, "supervisor-009")],
            "user:administrator");

        var governed = MesQualityHoldRequestContext.Resolve(context);

        Assert.Equal("user:supervisor-009", governed.Actor);
    }

    [Fact]
    public void Force_release_non_internal_token_with_internal_service_subject_ignores_forwarded_actor()
    {
        var context = CreateQualityHoldContext(
            [
                new Claim(ClaimTypes.NameIdentifier, "internal-service"),
                new Claim("token_type", "access"),
            ],
            "user:administrator");

        var governed = MesQualityHoldRequestContext.Resolve(context);

        Assert.Equal("user:internal-service", governed.Actor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("administrator")]
    [InlineData("user:")]
    [InlineData("user:   ")]
    [InlineData(":administrator")]
    [InlineData(" : ")]
    public void Force_release_internal_service_rejects_missing_or_non_canonical_forwarded_actor(
        string? forwardedActor)
    {
        var context = CreateQualityHoldContext(
            [
                new Claim(ClaimTypes.NameIdentifier, "internal-service"),
                new Claim("token_type", "internal_service"),
            ],
            forwardedActor);

        Assert.Throws<KnownException>(() => MesQualityHoldRequestContext.Resolve(context));
    }

    private static DefaultHttpContext CreateQualityHoldContext(
        IEnumerable<Claim> claims,
        string? forwardedActor)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };
        if (forwardedActor is not null)
        {
            context.Request.Headers["X-Authenticated-Actor"] = forwardedActor;
        }
        context.Request.Headers["X-Correlation-Id"] = "corr-force-trust";
        context.Request.Headers["X-Idempotency-Key"] = "idem-force-trust";
        return context;
    }

    [Fact]
    public void MesEndpointContracts_ExposeRescheduleAndRushOrderRoutes()
    {
        Assert.Equal(64, MesEndpointContracts.All.Count);
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/foundation-readiness/{areaCode}"
            && x.PermissionCode == MesPermissionCodes.FoundationRead
            && x.OperationId == "getBusinessMesFoundationReadinessArea");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/overview"
            && x.PermissionCode == MesPermissionCodes.OverviewRead
            && x.OperationId == "getBusinessMesOverview");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-plans"
            && x.PermissionCode == MesPermissionCodes.PlansRead
            && x.OperationId == "listBusinessMesProductionPlans");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-plans/{productionPlanId}/readiness"
            && x.PermissionCode == MesPermissionCodes.PlansRead
            && x.OperationId == "getBusinessMesProductionPlanReadiness");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/production-plans/{productionPlanId}/work-orders"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "convertBusinessMesPlanToWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/schedules/run"
            && x.PermissionCode == MesPermissionCodes.SchedulesManage
            && x.OperationId == "runBusinessMesSchedule");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/schedules"
            && x.PermissionCode == MesPermissionCodes.SchedulesRead
            && x.OperationId == "listBusinessMesScheduleResults");

        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/rush"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "createBusinessMesRushWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersRead
            && x.OperationId == "listBusinessMesWorkOrders");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersRead
            && x.OperationId == "getBusinessMesWorkOrderDetail");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/release"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "releaseBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/close"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "closeBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/hold"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "holdBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/cancel"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "cancelBusinessMesWorkOrder");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/engineering-change-decisions"
            && x.PermissionCode == MesPermissionCodes.WorkOrdersManage
            && x.OperationId == "recordBusinessMesEngineeringChangeDecision");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/quality-holds/{sourceDocumentId}/force-release"
            && x.PermissionCode == MesPermissionCodes.QualityWrite
            && x.OperationId == "forceReleaseBusinessMesQualityHold");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/quality-holds/{sourceDocumentId}/timeline"
            && x.PermissionCode == MesPermissionCodes.QualityRead
            && x.OperationId == "getBusinessMesQualityHoldTimeline");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness"
            && x.PermissionCode == MesPermissionCodes.MaterialsRead
            && x.OperationId == "getBusinessMesMaterialReadiness");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/work-orders/{workOrderId}/material-issue-requests"
            && x.PermissionCode == MesPermissionCodes.MaterialsManage
            && x.OperationId == "createBusinessMesMaterialIssueRequest");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/material-issue-requests"
            && x.PermissionCode == MesPermissionCodes.MaterialsRead
            && x.OperationId == "listBusinessMesMaterialIssueRequests");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/material-issue-requests/{requestId}"
            && x.PermissionCode == MesPermissionCodes.MaterialsRead
            && x.OperationId == "getBusinessMesMaterialIssueRequest");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/material-issue-requests/{requestId}/line-side-receipts"
            && x.PermissionCode == MesPermissionCodes.MaterialsManage
            && x.OperationId == "confirmBusinessMesLineSideMaterialReceipt");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/material-issue-requests/{requestId}/line-side-returns"
            && x.PermissionCode == MesPermissionCodes.MaterialsManage
            && x.OperationId == "returnBusinessMesLineSideMaterial");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/dispatch-tasks"
            && x.PermissionCode == MesPermissionCodes.DispatchRead
            && x.OperationId == "listBusinessMesDispatchTasks");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign"
            && x.PermissionCode == MesPermissionCodes.DispatchManage
            && x.OperationId == "assignBusinessMesDispatchTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/operation-tasks"
            && x.PermissionCode == MesPermissionCodes.OperationsRead
            && x.OperationId == "listBusinessMesOperationTasks");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/claim"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "claimBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/start"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "startBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/authorize-start"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "authorizeAndStartBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/pause"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "pauseBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/resume"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "resumeBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/operation-tasks/{operationTaskId}/complete"
            && x.PermissionCode == MesPermissionCodes.OperationsManage
            && x.OperationId == "completeBusinessMesOperationTask");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/wip"
            && x.PermissionCode == MesPermissionCodes.OperationsRead
            && x.OperationId == "getBusinessMesWipSummary");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/production-reports"
            && x.PermissionCode == MesPermissionCodes.ReportingWrite
            && x.OperationId == "recordBusinessMesProductionReport");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-reports"
            && x.PermissionCode == MesPermissionCodes.ReportingRead
            && x.OperationId == "listBusinessMesProductionReports");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-statistics"
            && x.PermissionCode == MesPermissionCodes.ReportingRead
            && x.OperationId == "queryBusinessMesProductionStatistics");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/production-reports/{reportNo}"
            && x.PermissionCode == MesPermissionCodes.ReportingRead
            && x.OperationId == "getBusinessMesProductionReport");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/production-reports/{reportNo}/reverse"
            && x.PermissionCode == MesPermissionCodes.ReportingWrite
            && x.OperationId == "reverseBusinessMesProductionReport");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/defects"
            && x.PermissionCode == MesPermissionCodes.QualityWrite
            && x.OperationId == "recordBusinessMesDefect");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/related-quality-items"
            && x.PermissionCode == MesPermissionCodes.QualityRead
            && x.OperationId == "listBusinessMesRelatedQualityItems");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/finished-goods-receipt-requests"
            && x.PermissionCode == MesPermissionCodes.ReceiptsManage
            && x.OperationId == "createBusinessMesFinishedGoodsReceiptRequest");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/finished-goods-receipt-requests"
            && x.PermissionCode == MesPermissionCodes.ReceiptsRead
            && x.OperationId == "listBusinessMesFinishedGoodsReceiptRequests");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/finished-goods-receipt-requests/{requestNo}/inventory-posting/retry"
            && x.PermissionCode == MesPermissionCodes.ReceiptsManage
            && x.OperationId == "retryBusinessMesFinishedGoodsReceiptInventoryPosting");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/capacity-impacts"
            && x.PermissionCode == MesPermissionCodes.CapacityRead
            && x.OperationId == "listBusinessMesCapacityImpacts");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/downtime-events"
            && x.PermissionCode == MesPermissionCodes.DowntimeRead
            && x.OperationId == "listBusinessMesDowntimeEvents");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/downtime-events"
            && x.PermissionCode == MesPermissionCodes.DowntimeManage
            && x.OperationId == "recordBusinessMesDowntimeEvent");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/downtime-events/{downtimeEventId}/recover"
            && x.PermissionCode == MesPermissionCodes.DowntimeManage
            && x.OperationId == "confirmBusinessMesDowntimeRecovery");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/shift-handovers"
            && x.PermissionCode == MesPermissionCodes.HandoversRead
            && x.OperationId == "listBusinessMesShiftHandovers");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/shift-handovers"
            && x.PermissionCode == MesPermissionCodes.HandoversManage
            && x.OperationId == "createBusinessMesShiftHandover");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "POST"
            && x.Route == "/api/business/v1/mes/shift-handovers/{handoverId}/accept"
            && x.PermissionCode == MesPermissionCodes.HandoversManage
            && x.OperationId == "acceptBusinessMesShiftHandover");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/traceability/work-orders/{workOrderId}"
            && x.PermissionCode == MesPermissionCodes.TraceabilityRead
            && x.OperationId == "getBusinessMesWorkOrderTraceability");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/traceability/batches/{batchOrSerial}"
            && x.PermissionCode == MesPermissionCodes.TraceabilityRead
            && x.OperationId == "getBusinessMesBatchTraceability");
        Assert.Contains(MesEndpointContracts.All, x =>
            x.HttpMethod == "GET"
            && x.Route == "/api/business/v1/mes/traceability/material-lots/{materialLotId}"
            && x.PermissionCode == MesPermissionCodes.TraceabilityRead
            && x.OperationId == "getBusinessMesMaterialLotTraceability");

        Assert.All(MesEndpointContracts.All, contract =>
            Assert.Contains(contract.PermissionCode, MesPermissionCodes.All));
    }

    [Fact]
    public async Task Work_order_lifecycle_commands_update_status_and_reject_illegal_close()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-05T08:00:00Z");
        var completed = WorkOrder.Create("org-001", "env-dev", "WO-CLOSE", "SKU-001", "PV-001", 2m, 10, now.AddDays(1));
        completed.MarkReleased();
        completed.Start(now);
        completed.RecordProductionProgress(2m, 0m, now.AddMinutes(30));
        var active = WorkOrder.Create("org-001", "env-dev", "WO-ACTIVE", "SKU-001", "PV-001", 2m, 10, now.AddDays(1));
        active.MarkReleased();
        dbContext.WorkOrders.AddRange(completed, active);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var closeHandler = new CloseWorkOrderCommandHandler(dbContext);
        var closeResponse = await closeHandler.Handle(
            new CloseWorkOrderCommand("org-001", "env-dev", "WO-CLOSE", now.AddHours(1)),
            CancellationToken.None);
        var holdResponse = await new HoldWorkOrderCommandHandler(dbContext).Handle(
            new HoldWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", "material shortage", now.AddMinutes(10)),
            CancellationToken.None);
        var cancelResponse = await new CancelWorkOrderCommandHandler(dbContext).Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", "plan cancelled", now.AddMinutes(20)),
            CancellationToken.None);
        var invalidClose = await Assert.ThrowsAsync<KnownException>(() => closeHandler.Handle(
            new CloseWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", now.AddHours(2)),
            CancellationToken.None));
        var duplicateCancelResponse = await new CancelWorkOrderCommandHandler(dbContext).Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-ACTIVE", "duplicate cancellation", now.AddMinutes(30)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("Accepted", closeResponse.Status);
        Assert.Equal("Accepted", holdResponse.Status);
        Assert.Equal("Accepted", cancelResponse.Status);
        Assert.Equal("Accepted", duplicateCancelResponse.Status);
        Assert.Equal(WorkOrder.ClosedStatus, completed.Status);
        Assert.Equal(now.AddHours(1), completed.ClosedAtUtc);
        Assert.Equal(WorkOrder.CancelledStatus, active.Status);
        Assert.Equal("material shortage", active.HoldReason);
        Assert.Equal("plan cancelled", active.CancelReason);
        Assert.NotEqual("duplicate cancellation", active.CancelReason);
        Assert.Contains("completed", invalidClose.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(invalidClose.InnerException);
    }

    [Fact]
    public async Task Cancel_released_work_order_cancels_open_material_receipt_and_operation_facts()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-07-03T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-695-LOCAL", "SKU-FG", "PV-001", 2m, 10, now.AddDays(1));
        workOrder.MarkReleased();
        var materialIssue = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            "MIR-695-LOCAL",
            "WO-695-LOCAL",
            "OP-10",
            "MAT-OIL",
            "L",
            2m,
            now);
        var receipt = FinishedGoodsReceiptRequest.Create(
            "org-001",
            "env-dev",
            "FGR-695-LOCAL",
            "WO-695-LOCAL",
            "SKU-FG",
            1m,
            "PCS",
            now);
        var operationTask = OperationTask.Queue(
            "org-001",
            "env-dev",
            "WO-695-LOCAL",
            "OP-10",
            10,
            "WC-10",
            [],
            now,
            TimeSpan.FromMinutes(30));
        operationTask.Assign(null, "DEV-695-LOCAL", null, now.AddMinutes(5), "user:dispatcher-695");
        operationTask.ClearDomainEvents();
        dbContext.WorkOrders.Add(workOrder);
        dbContext.MaterialIssueRequests.Add(materialIssue);
        dbContext.FinishedGoodsReceiptRequests.Add(receipt);
        dbContext.OperationTasks.Add(operationTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new CancelWorkOrderCommandHandler(dbContext).Handle(
            new CancelWorkOrderCommand(
                "org-001",
                "env-dev",
                "WO-695-LOCAL",
                "plan cancelled",
                now.AddMinutes(30),
                "user:endpoint-actor-695"),
            CancellationToken.None);

        Assert.Equal(WorkOrder.CancelledStatus, workOrder.Status);
        Assert.Equal(MaterialIssueRequest.CancelledStatus, materialIssue.Status);
        Assert.Equal(FinishedGoodsReceiptRequest.CancelledStatus, receipt.Status);
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled, operationTask.Status);
        var dispatchClearedEvent = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
            Assert.Single(operationTask.GetDomainEvents()));
        Assert.Equal(OperationTaskManualDispatchClearReason.OperationCancelled, dispatchClearedEvent.Reason);
        Assert.Equal("user:endpoint-actor-695", dispatchClearedEvent.Actor);
        var cancelledEvent = Assert.IsType<WorkOrderCancelledDomainEvent>(workOrder.GetDomainEvents().Last());
        Assert.Equal(["MIR-695-LOCAL"], cancelledEvent.MaterialIssueRequestNos);
    }

    [Fact]
    public async Task Starting_operation_task_starts_owning_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-05T08:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-START", "SKU-001", "PV-001", 2m, 10, now.AddDays(1));
        var tasks = workOrder.Release(
            now,
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-001", [], TimeSpan.FromMinutes(30)),
            ]);
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            now);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-10", "start", now.AddMinutes(5)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("OP-10", response.OperationTaskId);
        Assert.Equal(WorkOrder.StartedStatus, workOrder.Status);
    }

    [Fact]
    public async Task Mes_workbench_queries_return_detail_operations_wip_and_empty_material_context()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-05-24T08:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-FG-1000",
            "PV-001",
            10m,
            1,
            dueUtc);
        var tasks = workOrder.Release(
            dueUtc.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30),
                    OperationCode: "OP-MIX"),
            ]);
        tasks.Single().Assign(null, "device-asset-cnc-01", null, dueUtc.AddMinutes(-40));
        workOrder.Start(dueUtc.AddMinutes(-30));
        tasks.Single().Start(dueUtc.AddMinutes(-30));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        var scrapLots = SeedReceivedMaterialIssue(dbContext, "WO-001", "OP-10", "MIR-WIP-SCRAP", dueUtc.AddMinutes(-20), 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordProductionReportCommandHandler(dbContext, TestProductionReportOeeDimensionSnapshotProvider.Instance).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 8m, 1m, false, dueUtc, ConsumedMaterialLots: scrapLots),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);
        var operations = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);
        var wip = await new GetWipSummaryQueryHandler(dbContext).Handle(
            new GetWipSummaryQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);
        var material = await new GetMaterialReadinessQueryHandler(dbContext).Handle(
            new GetMaterialReadinessQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);

        Assert.Equal("WO-001", detail.WorkOrderId);
        Assert.Equal("Ready", detail.ReadinessStatus);
        Assert.Empty(detail.BlockingReasons);
        var detailOperation = Assert.Single(detail.OperationTasks);
        Assert.Equal("OP-10", detailOperation.OperationTaskId);
        Assert.Equal("WO-001", detailOperation.WorkOrderId);
        Assert.Null(detailOperation.WorkOrderNo);
        Assert.Null(detailOperation.OperationTaskNo);
        Assert.Equal("device-asset-cnc-01", detailOperation.DeviceAssetId);
        Assert.Null(detailOperation.DeviceAssetCode);
        Assert.Null(detailOperation.DeviceAssetName);
        Assert.Equal("OP-MIX", detailOperation.OperationCode);
        var operation = Assert.Single(operations.Items);
        Assert.Equal("OP-10", operation.OperationTaskId);
        Assert.Equal("WO-001", operation.WorkOrderId);
        Assert.Null(operation.WorkOrderNo);
        Assert.Null(operation.OperationTaskNo);
        Assert.Equal("device-asset-cnc-01", operation.DeviceAssetId);
        Assert.Null(operation.DeviceAssetCode);
        Assert.Null(operation.DeviceAssetName);
        Assert.Equal("OP-MIX", operation.OperationCode);
        var wipRow = Assert.Single(wip.Items);
        Assert.Equal(10m, wipRow.PlannedQuantity);
        Assert.Equal(8m, wipRow.GoodQuantity);
        Assert.Equal(1m, wipRow.ScrapQuantity);
        Assert.Equal("Ready", material.ReadinessStatus);
        Assert.Empty(material.Items);
    }

    [Fact]
    public async Task List_operation_tasks_filters_items_and_total_by_scope_and_exact_work_order_id()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-07-27T08:00:00Z");
        var workOrderA = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-EXACT-A",
            "SKU-A",
            "PV-A",
            1m,
            1,
            dueUtc);
        var workOrderB = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-EXACT-B",
            "SKU-B",
            "PV-B",
            1m,
            2,
            dueUtc);
        var otherScopeWorkOrderA = WorkOrder.Create(
            "org-002",
            "env-dev",
            "WO-EXACT-A",
            "SKU-A",
            "PV-A",
            1m,
            3,
            dueUtc);
        var otherEnvironmentWorkOrderA = WorkOrder.Create(
            "org-001",
            "env-qa",
            "WO-EXACT-A",
            "SKU-A",
            "PV-A",
            1m,
            4,
            dueUtc);
        dbContext.WorkOrders.AddRange(
            workOrderA,
            workOrderB,
            otherScopeWorkOrderA,
            otherEnvironmentWorkOrderA);
        dbContext.OperationTasks.AddRange(workOrderA.Release(dueUtc.AddHours(-1), [
            new RoutingStepSnapshot("OP-A", 10, "WC-A", [], TimeSpan.FromMinutes(10)),
        ]));
        dbContext.OperationTasks.AddRange(workOrderB.Release(dueUtc.AddHours(-1), [
            new RoutingStepSnapshot("OP-B-1", 10, "WC-B", [], TimeSpan.FromMinutes(10)),
            new RoutingStepSnapshot("OP-B-2", 20, "WC-B", [], TimeSpan.FromMinutes(10)),
        ]));
        dbContext.OperationTasks.AddRange(otherScopeWorkOrderA.Release(dueUtc.AddHours(-1), [
            new RoutingStepSnapshot("OP-OTHER-SCOPE", 10, "WC-A", [], TimeSpan.FromMinutes(10)),
        ]));
        dbContext.OperationTasks.AddRange(otherEnvironmentWorkOrderA.Release(dueUtc.AddHours(-1), [
            new RoutingStepSnapshot("OP-OTHER-ENV", 10, "WC-A", [], TimeSpan.FromMinutes(10)),
        ]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery(
                "org-001",
                "env-dev",
                null,
                Take: 100,
                WorkOrderId: "WO-EXACT-A"),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal("WO-EXACT-A", item.WorkOrderId);
        Assert.Equal("OP-A", item.OperationTaskId);
        Assert.Null(item.OperationTaskNo);
    }

    [Fact]
    public async Task List_operation_tasks_endpoint_forwards_exact_strong_id_pair()
    {
        var sender = new CapturingListOperationTasksSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.GetAsync(
            "/api/business/v1/mes/operation-tasks?organizationId=org-001&environmentId=env-dev&workOrderId=WO-EXACT-A&operationTaskId=OP-EXACT-A&skip=0&take=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(sender.Query);
        Assert.Equal("org-001", sender.Query.OrganizationId);
        Assert.Equal("env-dev", sender.Query.EnvironmentId);
        Assert.Equal("WO-EXACT-A", sender.Query.WorkOrderId);
        Assert.Equal("OP-EXACT-A", sender.Query.OperationTaskId);
    }

    [Fact]
    public async Task Assign_dispatch_task_endpoint_forwards_collaboration_participants()
    {
        var sender = new CapturingAssignDispatchTaskSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(sender);
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:planner");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/dispatch-tasks/OP-HTTP/assign",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                assignedUserId = "worker-a",
                participants = new[]
                {
                    new { workerId = "worker-a", workerName = "Alice", sharePercent = 60m },
                    new { workerId = "worker-b", workerName = "Bob", sharePercent = 40m },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<AssignDispatchTaskCommand>(sender.Command);
        Assert.Equal("OP-HTTP", command.OperationTaskId);
        Assert.Equal("user:planner", command.Actor);
        Assert.Collection(
            Assert.IsAssignableFrom<IReadOnlyCollection<DispatchParticipantInput>>(command.Participants),
            first => Assert.Equal(("worker-a", "Alice", 60m), (first.WorkerId, first.WorkerName, first.SharePercent)),
            second => Assert.Equal(("worker-b", "Bob", 40m), (second.WorkerId, second.WorkerName, second.SharePercent)));
    }

    [Fact]
    public async Task Production_report_only_rolls_work_order_progress_from_output_operation()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-05-24T09:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-OUTPUT",
            "SKU-FG-1000",
            "PV-001",
            100m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-20",
                    20,
                    "WC-INSPECT-01",
                    [],
                    TimeSpan.FromMinutes(20)),
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-30",
                    30,
                    "WC-PACK-01",
                    [],
                    TimeSpan.FromMinutes(25)),
            ]);
        workOrder.Start(reportedAt.AddMinutes(-20));
        tasks.Single(x => x.OperationTaskId == "OP-10").Start(reportedAt.AddMinutes(-15));
        tasks.Single(x => x.OperationTaskId == "OP-20").Start(reportedAt.AddMinutes(10));
        tasks.Single(x => x.OperationTaskId == "OP-30").Start(reportedAt.AddMinutes(25));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        var scrapLots = SeedReceivedMaterialIssue(dbContext, "WO-OUTPUT", "OP-30", "MIR-OUTPUT-SCRAP", reportedAt.AddMinutes(20), 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordProductionReportCommandHandler(dbContext, TestProductionReportOeeDimensionSnapshotProvider.Instance);
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-10", 100m, 0m, true, reportedAt),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(WorkOrder.StartedStatus, workOrder.Status);
        Assert.Equal(
            Domain.AggregatesModel.OperationTaskAggregate.OperationTaskLifecycleStatus.Completed,
            tasks.Single(x => x.OperationTaskId == "OP-10").Status);

        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-20", 0m, 0m, true, reportedAt.AddMinutes(20), ReworkQuantity: 1m),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-30", 40m, 0m, false, reportedAt.AddMinutes(30)),
            CancellationToken.None);
        await handler.Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-30", 59m, 1m, true, reportedAt.AddMinutes(45), ConsumedMaterialLots: scrapLots),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(99m, workOrder.CompletedQuantity);
        Assert.Equal(1m, workOrder.ScrapQuantity);
        Assert.Equal(WorkOrder.CompletedStatus, workOrder.Status);
    }

    [Fact]
    public async Task Production_report_rejects_non_completion_report_for_operation_outside_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-05-24T10:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-OUTPUT",
            "SKU-FG-1000",
            "PV-001",
            100m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        workOrder.Start(reportedAt.AddMinutes(-20));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<KnownException>(() => new RecordProductionReportCommandHandler(dbContext, TestProductionReportOeeDimensionSnapshotProvider.Instance).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-OUTPUT", "OP-404", 1m, 0m, false, reportedAt),
            CancellationToken.None));

        Assert.Contains("报工工序任务不存在或不属于当前工单", exception.Message, StringComparison.Ordinal);
        Assert.Empty(dbContext.ProductionReports);
        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(0m, workOrder.ScrapQuantity);
    }

    [Fact]
    public async Task Production_report_completion_rejects_non_running_task_before_recording_side_effects()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-07-27T10:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-STATE",
            "SKU-FG-1000",
            "PV-001",
            100m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new RecordProductionReportCommandHandler(dbContext, TestProductionReportOeeDimensionSnapshotProvider.Instance).Handle(
                new RecordProductionReportCommand(
                    "org-001",
                    "env-dev",
                    "WO-STATE",
                    "OP-10",
                    1m,
                    0m,
                    true,
                    reportedAt),
                CancellationToken.None));

        Assert.Equal("report", exception.Action);
        Assert.Equal(nameof(OperationTaskLifecycleStatus.Queued), exception.CurrentStatus);
        Assert.Empty(dbContext.ProductionReports);
        Assert.Equal(0m, workOrder.CompletedQuantity);
        Assert.Equal(0m, workOrder.ScrapQuantity);
    }

    [Fact]
    public void Operation_task_status_filters_do_not_depend_on_enum_ToString_provider_translation()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=nerv_iip_query_translation;Username=nerv;Password=nerv")
            .Options;
        using var dbContext = new Infrastructure.ApplicationDbContext(options, new NoopMediator());

        var query = InvokeOperationTaskEntityQuery(
            dbContext,
            "org-001",
            "env-dev",
            null,
            "inProgress",
            "progress",
            null,
            null,
            null);

        Assert.DoesNotContain("ToString", query.Expression.ToString(), StringComparison.Ordinal);

        var sql = query.ToQueryString();
        Assert.Contains("operation_tasks", sql, StringComparison.Ordinal);
        Assert.Contains("status", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Traceability_active_report_filters_translate_to_npgsql_exists_queries()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=nerv_iip_query_translation;Username=nerv;Password=nerv")
            .Options;
        using var dbContext = new Infrastructure.ApplicationDbContext(options, new NoopMediator());

        var activeProductionReports = dbContext.ActiveProductionReports();
        var materialLotSql = dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.MaterialLotId == "LOT-001" &&
                activeProductionReports.Any(report =>
                    report.OrganizationId == x.OrganizationId &&
                    report.EnvironmentId == x.EnvironmentId &&
                    report.ReportNo == x.ReportNo))
            .Select(x => x.ReportNo)
            .ToQueryString();
        var producedBatchSql = activeProductionReports
            .Where(x =>
                x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                (x.ProducedLotNo == "LOT-001" || x.SerialNo == "LOT-001"))
            .Select(x => x.ReportNo)
            .ToQueryString();

        Assert.Contains("EXISTS", materialLotSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS", materialLotSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reversed_report_no", materialLotSql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", producedBatchSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reversed_report_no", producedBatchSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Finished_goods_receipt_cumulative_quantity_guard_translates_to_npgsql_sum_query()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=nerv_iip_query_translation;Username=nerv;Password=nerv")
            .Options;
        using var dbContext = new Infrastructure.ApplicationDbContext(options, new NoopMediator());

        var sql = CreateFinishedGoodsReceiptRequestCommandHandler.ActiveReceiptRequestsForWorkOrder(
                dbContext.FinishedGoodsReceiptRequests,
                "org-001",
                "env-dev",
                "WO-001")
            .GroupBy(_ => 1)
            .Select(group => group.Sum(x => x.Quantity))
            .ToQueryString();

        Assert.Contains("finished_goods_receipt_requests", sql, StringComparison.Ordinal);
        Assert.Contains("sum", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", sql, StringComparison.Ordinal);
        Assert.Contains("Cancelled", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_plan_to_work_order_persists_demand_planning_source_reference_for_queries_and_traceability()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var requestedAtUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");

        var response = await new ConvertPlanToWorkOrderCommandHandler(dbContext).Handle(
            new ConvertPlanToWorkOrderCommand(
                "org-001",
                "env-dev",
                "SUG-001",
                "WO-DP-001",
                requestedAtUtc,
                "SKU-FG-1000",
                "PV-001",
                12m,
                "PCS",
                requestedAtUtc.AddDays(2),
                "WC-MIX-01",
                "DemandPlanning",
                "PlanningSuggestion",
                "SUG-001",
                "DEMAND-001",
                "convert-dp-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal("WO-DP-001", response.ReferenceId);
        Assert.NotNull(workOrder.SourcePlanReference);
        Assert.Equal("DemandPlanning", workOrder.SourcePlanReference.SourceSystem);
        Assert.Equal("PlanningSuggestion", workOrder.SourcePlanReference.SourceDocumentType);
        Assert.Equal("SUG-001", workOrder.SourcePlanReference.SourceDocumentId);
        Assert.Equal("DEMAND-001", workOrder.SourcePlanReference.SourceDemandReference);

        var plans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Take: 100),
            CancellationToken.None);
        var plan = Assert.Single(plans.Items);
        Assert.Equal("SUG-001", plan.ProductionPlanId);
        Assert.Equal("DemandPlanning", plan.SourceSystem);
        Assert.Equal("PlanningSuggestion", plan.SourceDocumentType);
        Assert.Equal("SUG-001", plan.SourceDocumentId);
        Assert.Equal("DEMAND-001", plan.SourceDemandReference);
        Assert.Equal("created", plan.Status);

        var detail = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery("org-001", "env-dev", "WO-DP-001"),
            CancellationToken.None);
        Assert.Equal("DemandPlanning", detail.SourcePlanReference?.SourceSystem);
        Assert.Equal("SUG-001", detail.SourcePlanReference?.SourceDocumentId);
        Assert.Equal("DEMAND-001", detail.SourcePlanReference?.SourceDemandReference);

        var traceability = await new GetWorkOrderTraceabilityQueryHandler(dbContext).Handle(
            new GetWorkOrderTraceabilityQuery("org-001", "env-dev", "WO-DP-001"),
            CancellationToken.None);
        Assert.Contains(traceability.Nodes, x => x.NodeId == "SUG-001" && x.NodeType == "PlanningSuggestion");
        Assert.Contains(traceability.Edges, x => x.FromNodeId == "SUG-001" && x.ToNodeId == "WO-DP-001" && x.RelationType == "converted-to-work-order");
    }

    [Fact]
    public async Task Production_plan_query_filters_status_before_take_and_uses_work_order_status()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-CREATED-001",
            "SKU-001",
            "PV-001",
            1m,
            10,
            dueUtc,
            "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-CREATED-001", null)));
        var released = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-RELEASED-001",
            "SKU-002",
            "PV-002",
            1m,
            10,
            dueUtc.AddMinutes(1),
            "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-RELEASED-001", null));
        released.Release(
            dueUtc,
            [
                new RoutingStepSnapshot("OP-10", 10, "WC-01", [], TimeSpan.FromMinutes(30)),
            ]);
        dbContext.WorkOrders.Add(released);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var plans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", "released", Take: 1),
            CancellationToken.None);

        var plan = Assert.Single(plans.Items);
        Assert.Equal("SUG-RELEASED-001", plan.ProductionPlanId);
        Assert.Equal("released", plan.Status);
    }

    [Fact]
    public async Task Production_plan_query_filters_source_and_readiness_before_count_and_page()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-SALES-001",
            "SKU-SALES",
            "PV-001",
            1m,
            10,
            dueUtc,
            "PCS",
            new SourcePlanReference("SalesOrder", "PlanningSuggestion", "SO-001", "DEMAND-SALES")));
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-STOCK-001",
            "SKU-STOCK",
            "PV-001",
            1m,
            10,
            dueUtc.AddMinutes(1),
            "PCS",
            new SourcePlanReference("StockPlan", "PlanningSuggestion", "STOCK-001", "DEMAND-STOCK")));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var salesPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "SalesOrder", Source: "sales", ReadinessStatus: "Ready"),
            CancellationToken.None);
        var blockedPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Source: "sales", ReadinessStatus: "Blocked"),
            CancellationToken.None);

        Assert.Equal(1, salesPlans.Total);
        Assert.Equal("SO-001", Assert.Single(salesPlans.Items).ProductionPlanId);
        Assert.Equal(0, blockedPlans.Total);
        Assert.Empty(blockedPlans.Items);
    }

    [Fact]
    public async Task Production_plan_keyword_does_not_bypass_filters_with_readiness_text()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-ALPHA-001",
            "SKU-ALPHA",
            "PV-ALPHA",
            1m,
            10,
            dueUtc,
            "PCS",
            new SourcePlanReference("Alpha", "Beta", "GAMMA-001", "DELTA-001")));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var substringPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "y"),
            CancellationToken.None);
        var readyPlans = await new ListProductionPlansQueryHandler(dbContext).Handle(
            new ListProductionPlansQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "ready"),
            CancellationToken.None);

        Assert.Equal(0, substringPlans.Total);
        Assert.Empty(substringPlans.Items);
        Assert.Equal(0, readyPlans.Total);
        Assert.Empty(readyPlans.Items);
    }

    [Fact]
    public async Task Work_order_list_query_returns_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var dueUtc = DateTimeOffset.Parse("2026-06-01T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 1m, 10, dueUtc));
        var partiallyCompleted = WorkOrder.Create("org-001", "env-dev", "WO-002", "SKU-002", "PV-002", 3m, 10, dueUtc.AddMinutes(1), "PCS");
        partiallyCompleted.RecordProductionProgress(1m, 0m, dueUtc.AddMinutes(2));
        dbContext.WorkOrders.Add(partiallyCompleted);
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-003", "SKU-003", "PV-003", 1m, 10, dueUtc.AddMinutes(2)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var page = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null, Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, page.Total);
        var workOrder = Assert.Single(page.Items);
        Assert.Equal("WO-002", workOrder.WorkOrderId);
        Assert.Equal("PCS", workOrder.UomCode);
        Assert.Equal(1m, workOrder.CompletedQuantity);
    }

    /// <summary>
    /// #1947：停机原因汇总的名次是「时长降序、同时长按原因码升序」。用 InMemory 是为了让分组
    /// 到达顺序确定（等于写入顺序）——真实 PostgreSQL 的 GROUP BY 行序未定义，删掉平局键之后
    /// 得到的顺序是碰运气的，写不出可靠转红的用例。聚合本身在
    /// <see cref="MesDowntimeReadFacePostgresTests"/> 用真库证明。
    /// </summary>
    [Fact]
    public async Task Downtime_reason_summary_breaks_equal_duration_ties_by_reason_code()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var from = DateTimeOffset.Parse("2026-07-30T08:00:00Z");
        // 写入顺序刻意是 DT-PM 在 DT-MECH 之前，与期望名次相反：平局键被删掉时稳定排序会保留写入顺序。
        dbContext.WorkCenterUnavailabilities.AddRange(
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
                "org-001", "env-dev", "DT-PM-1", "WC-12", from, from.AddMinutes(60), "DT-PM", "EQ-012"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
                "org-001", "env-dev", "DT-MECH-1", "WC-10", from, from.AddMinutes(60), "DT-MECH", "EQ-010"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListDowntimeEventsQueryHandler(dbContext, TimeProvider.System).Handle(
            new ListDowntimeEventsQuery(
                "org-001",
                "env-dev",
                null,
                null,
                WindowStartUtc: from.AddDays(-1),
                WindowEndUtc: from.AddDays(1)),
            CancellationToken.None);

        Assert.Equal(new[] { 60m, 60m }, response.ReasonSummary.Select(x => x.DurationMinutes));
        Assert.Equal(new[] { "DT-MECH", "DT-PM" }, response.ReasonSummary.Select(x => x.ReasonCode));
    }

    [Fact]
    public async Task Secondary_mes_list_queries_return_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.MaterialIssueRequests.AddRange(
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-001", "WO-MAT", "OP-MAT-10", "MAT-OIL", "L", 1m, now.AddMinutes(1)),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-002", "WO-MAT", "OP-MAT-20", "MAT-OIL", "L", 1m, now.AddMinutes(2)),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-003", "WO-MAT", "OP-MAT-30", "MAT-OIL", "L", 1m, now.AddMinutes(3)));
        dbContext.WorkCenterUnavailabilities.AddRange(
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-001", "WC-MIX", now.AddMinutes(1), null, "breakdown", "ASSET-001"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-002", "WC-MIX", now.AddMinutes(2), null, "breakdown", "ASSET-001"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-003", "WC-MIX", now.AddMinutes(3), null, "breakdown", "ASSET-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var materialIssues = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", "WO-MAT", Skip: 1, Take: 1),
            CancellationToken.None);
        var downtimeEvents = await new ListDowntimeEventsQueryHandler(dbContext, TimeProvider.System).Handle(
            new ListDowntimeEventsQuery(
                "org-001",
                "env-dev",
                "WC-MIX",
                "ASSET-001",
                Skip: 1,
                Take: 1,
                WindowStartUtc: now.AddDays(-1),
                WindowEndUtc: now.AddDays(1)),
            CancellationToken.None);
        var capacityImpacts = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", "ASSET-001", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, materialIssues.Total);
        Assert.Equal("MIR-002", Assert.Single(materialIssues.Items).RequestId);
        Assert.Equal(3, downtimeEvents.Total);
        Assert.Equal("DOWNTIME-002", Assert.Single(downtimeEvents.Items).DowntimeEventId);
        Assert.Equal(3, capacityImpacts.Total);
        Assert.Equal("DOWNTIME-002", Assert.Single(capacityImpacts.Items).ImpactId);
    }

    [Fact]
    public async Task Material_issue_creation_validates_supplementary_source_scope_and_chain()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        dbContext.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-SUP-001", "SKU-001", "PV-001", 10m, 10, now),
            WorkOrder.Create("org-001", "env-dev", "WO-SUP-002", "SKU-002", "PV-002", 10m, 10, now),
            WorkOrder.Create("org-002", "env-dev", "WO-SUP-001", "SKU-001", "PV-001", 10m, 10, now));
        dbContext.MaterialIssueRequests.AddRange(
            MaterialIssueRequest.Create("org-001", "env-dev", "MIR-ORIGINAL-001", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 2m, now),
            MaterialIssueRequest.Create("org-001", "env-dev", "MIR-SUPPLEMENTARY-001", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now.AddMinutes(1), true, "MIR-ORIGINAL-001"),
            MaterialIssueRequest.Create("org-002", "env-dev", "MIR-OTHER-SCOPE", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 2m, now));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", null,
            2m, 0m, 0m, "MBOM", "SNAP-SUP-001", now, []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateMaterialIssueRequestCommandHandler(dbContext);
        var normal = await handler.Handle(
            new CreateMaterialIssueRequestCommand("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now.AddMinutes(2)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var normalRow = await dbContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == normal.ReferenceId);
        Assert.False(normalRow.IsSupplementary);
        Assert.Null(normalRow.OriginalMaterialIssueRequestNo);

        var supplementary = await handler.Handle(
            new CreateMaterialIssueRequestCommand("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now.AddMinutes(3), null, true, "MIR-ORIGINAL-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var supplementaryRow = await dbContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == supplementary.ReferenceId);
        Assert.True(supplementaryRow.IsSupplementary);
        Assert.Equal("MIR-ORIGINAL-001", supplementaryRow.OriginalMaterialIssueRequestNo);

        var rejectedCases = new (string Name, CreateMaterialIssueRequestCommand Command, string Message)[]
        {
            ("missing source", new("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now, null, true, null), "必须指定原领料单"),
            ("cross scope", new("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now, null, true, "MIR-OTHER-SCOPE"), "不存在或不在当前组织"),
            ("cross work order", new("org-001", "env-dev", "WO-SUP-002", "OP-10", "MAT-001", "PCS", 1m, now, null, true, "MIR-ORIGINAL-001"), "不存在或不在当前组织"),
            ("cross material", new("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-002", "PCS", 1m, now, null, true, "MIR-ORIGINAL-001"), "不存在或不在当前组织"),
            ("supplementary chain", new("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now, null, true, "MIR-SUPPLEMENTARY-001"), "不能再次关联补料单"),
            ("ordinary with source", new("org-001", "env-dev", "WO-SUP-001", "OP-10", "MAT-001", "PCS", 1m, now, null, false, "MIR-ORIGINAL-001"), "普通领料申请不能指定")
        };
        foreach (var rejected in rejectedCases)
        {
            var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(rejected.Command, CancellationToken.None));
            Assert.Contains(rejected.Message, exception.Message, StringComparison.Ordinal);
        }
    }

    // Contract: DomainInvariant + Regression. Authority: Issue #2224 acceptance 1-2.
    [Fact]
    public async Task Material_issue_creation_resolves_actual_sku_only_from_latest_scoped_frozen_requirements()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-08-27T08:00:00Z");
        dbContext.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-SUB", "SKU-FG", "PV-001", 10m, 10, now),
            WorkOrder.Create("org-001", "env-dev", "WO-AMB", "SKU-FG", "PV-001", 10m, 10, now),
            WorkOrder.Create("org-001", "env-dev", "WO-SCOPE", "SKU-FG", "PV-001", 10m, 10, now));
        dbContext.MaterialRequirements.AddRange(
            // Same identity: the older row must neither authorize MAT-STALE nor contribute quantity.
            MaterialRequirement.Capture("org-001", "env-dev", "WO-SUB", "OP-10", "MAT-PRIMARY", null, 100m, 0m, 0m, "MBOM", "SNAP-OLD", now.AddMinutes(-3), ["MAT-STALE"]),
            MaterialRequirement.Capture("org-001", "env-dev", "WO-SUB", "OP-10", "MAT-PRIMARY", null, 4m, 0m, 0m, "MBOM", "SNAP-LATEST", now, ["MAT-ALT"]),
            // A distinct lot identity of the same primary is additive, not ambiguous.
            MaterialRequirement.Capture("org-001", "env-dev", "WO-SUB", "OP-10", "MAT-PRIMARY", "LOT-B", 6m, 0m, 0m, "MBOM", "SNAP-LOT-B", now, ["MAT-ALT"]),
            // Work-order-level requirements are eligible for an operation-scoped request.
            MaterialRequirement.Capture("org-001", "env-dev", "WO-SUB", null, "MAT-PRIMARY", "LOT-C", 2m, 0m, 0m, "MBOM", "SNAP-WO", now, ["MAT-ALT"]),
            // A different task must not make OP-10 ambiguous.
            MaterialRequirement.Capture("org-001", "env-dev", "WO-SUB", "OP-20", "MAT-OTHER", null, 8m, 0m, 0m, "MBOM", "SNAP-OP-20", now, ["MAT-ALT"]),
            MaterialRequirement.Capture("org-001", "env-dev", "WO-AMB", "OP-10", "MAT-A", null, 3m, 0m, 0m, "MBOM", "SNAP-A", now, ["MAT-AMB"]),
            MaterialRequirement.Capture("org-001", "env-dev", "WO-AMB", "OP-10", "MAT-B", null, 5m, 0m, 0m, "MBOM", "SNAP-B", now, ["MAT-AMB"]),
            MaterialRequirement.Capture("org-002", "env-dev", "WO-SCOPE", "OP-10", "MAT-SCOPED", null, 7m, 0m, 0m, "MBOM", "SNAP-SCOPE", now, ["MAT-CROSS-SCOPE"]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateMaterialIssueRequestCommandHandler(dbContext);
        var validRequest = new CreateMaterialIssueRequestCommand(
            "org-001", "env-dev", "WO-SUB", "OP-10", "MAT-ALT", "PCS", null, now.AddMinutes(1),
            "valid-material-replay");
        var accepted = await handler.Handle(validRequest, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var substitute = await dbContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == accepted.ReferenceId);
        Assert.Equal("MAT-ALT", substitute.MaterialId);
        Assert.Equal("MAT-PRIMARY", substitute.SubstitutedMaterialId);
        Assert.Equal(12m, substitute.RequestedQuantity);
        Assert.Equal(
            accepted.ReferenceId,
            (await handler.Handle(validRequest, CancellationToken.None)).ReferenceId);
        Assert.Single(dbContext.MaterialIssueRequests.Where(x => x.RequestNo == accepted.ReferenceId));

        var direct = await handler.Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001", "env-dev", "WO-SUB", "OP-10", "MAT-PRIMARY", "PCS", 1m, now.AddMinutes(2)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        Assert.Null((await dbContext.MaterialIssueRequests.SingleAsync(x => x.RequestNo == direct.ReferenceId)).SubstitutedMaterialId);

        var rejectedCases = new (string Name, CreateMaterialIssueRequestCommand Command, string Message)[]
        {
            ("stale candidate", new("org-001", "env-dev", "WO-SUB", "OP-10", "MAT-STALE", "PCS", 1m, now), "不属于该工单冻结"),
            ("non-candidate", new("org-001", "env-dev", "WO-SUB", "OP-10", "MAT-UNKNOWN", "PCS", 1m, now), "不属于该工单冻结"),
            ("cross scope", new("org-001", "env-dev", "WO-SCOPE", "OP-10", "MAT-CROSS-SCOPE", "PCS", 1m, now), "不属于该工单冻结"),
            ("ambiguous", new("org-001", "env-dev", "WO-AMB", "OP-10", "MAT-AMB", "PCS", 1m, now), "对应多个主料")
        };
        foreach (var rejected in rejectedCases)
        {
            var exception = await Assert.ThrowsAsync<KnownException>(
                () => handler.Handle(rejected.Command, CancellationToken.None));
            Assert.Contains(rejected.Message, exception.Message, StringComparison.Ordinal);
        }

        var rejectedReplay = new CreateMaterialIssueRequestCommand(
            "org-001", "env-dev", "WO-SUB", "OP-10", "MAT-UNKNOWN", "PCS", 1m, now,
            "rejected-material-replay");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var exception = await Assert.ThrowsAsync<KnownException>(
                () => handler.Handle(rejectedReplay, CancellationToken.None));
            Assert.Contains("不属于该工单冻结", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Material_issue_list_exposes_supplementary_fields_and_unpaged_filtered_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-08-25T08:00:00Z");
        dbContext.MaterialIssueRequests.AddRange(
            MaterialIssueRequest.Create("org-001", "env-dev", "MIR-LIST-ORIGINAL", "WO-LIST", "OP-10", "MAT-LIST", "PCS", 2m, now),
            MaterialIssueRequest.Create("org-001", "env-dev", "MIR-LIST-SUP-1", "WO-LIST", "OP-10", "MAT-LIST", "PCS", 1m, now.AddMinutes(1), true, "MIR-LIST-ORIGINAL"),
            MaterialIssueRequest.Create("org-001", "env-dev", "MIR-LIST-SUP-2", "WO-LIST", "OP-20", "MAT-LIST", "PCS", 1m, now.AddMinutes(2), true, "MIR-LIST-ORIGINAL"),
            MaterialIssueRequest.Create("org-002", "env-dev", "MIR-LIST-OTHER-SCOPE", "WO-LIST", "OP-10", "MAT-LIST", "PCS", 1m, now, true, "MIR-LIST-ORIGINAL"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", "WO-LIST", Skip: 1, Take: 1, Keyword: "MAT-LIST"),
            CancellationToken.None);

        Assert.Equal(3, response.Total);
        Assert.Equal(2, response.SupplementaryCount);
        var row = Assert.Single(response.Items);
        Assert.True(row.IsSupplementary);
        Assert.Equal("MIR-LIST-ORIGINAL", row.OriginalMaterialIssueRequestNo);
    }

    // Contract: HttpApi + Isolation + Regression. Authority: Issue #2224 acceptance 1-2.
    [Fact]
    public async Task Material_issue_http_create_list_and_detail_preserve_substitute_audit_and_scope()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-08-27T09:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-SUB-HTTP", "SKU-FG", "PV-001", 10m, 10, now));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            "org-001", "env-dev", "WO-SUB-HTTP", "OP-10", "MAT-PRIMARY", null,
            4m, 0m, 0m, "MBOM", "SNAP-HTTP", now, ["MAT-ALT"]));
        dbContext.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
            "org-002", "env-dev", "MIR-SUB-OTHER", "WO-SUB-HTTP", "OP-10", "MAT-ALT", "PCS", 4m, now,
            substitutedMaterialId: "MAT-OTHER-PRIMARY"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new MaterialIssueHttpSender(dbContext));
                });
            });
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var createdResponse = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-SUB-HTTP/material-issue-requests",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                operationTaskId = "OP-10",
                materialId = "MAT-ALT",
                uomCode = "PCS",
                quantity = (decimal?)null,
                requestedAtUtc = now,
            });
        Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
        using var createdBody = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync());
        var requestNo = createdBody.RootElement.GetProperty("referenceId").GetString()!;

        var listResponse = await client.GetAsync(
            "/api/business/v1/mes/material-issue-requests?organizationId=org-001&environmentId=env-dev&workOrderId=WO-SUB-HTTP");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listRow = Assert.Single(listBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("MAT-ALT", listRow.GetProperty("materialId").GetString());
        Assert.Equal("MAT-PRIMARY", listRow.GetProperty("substitutedMaterialId").GetString());

        var detailResponse = await client.GetAsync(
            $"/api/business/v1/mes/material-issue-requests/{requestNo}?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailBody = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("MAT-ALT", detailBody.RootElement.GetProperty("materialId").GetString());
        Assert.Equal("MAT-PRIMARY", detailBody.RootElement.GetProperty("substitutedMaterialId").GetString());

        var crossScopeResponse = await client.GetAsync(
            "/api/business/v1/mes/material-issue-requests/MIR-SUB-OTHER?organizationId=org-001&environmentId=env-dev");
        var crossScopeBody = await crossScopeResponse.Content.ReadAsStringAsync();
        Assert.Contains("未找到领料申请", crossScopeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("MAT-OTHER-PRIMARY", crossScopeBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Material_issue_query_for_operation_returns_operation_and_work_order_level_received_requests()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        var operationRequest = Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-OP-001", "WO-MAT", "OP-MAT-10", "MAT-OIL", "L", 1m, now);
        operationRequest.ConfirmAndPostLineSideReceipt(
            new Domain.AggregatesModel.MaterialSupplyAggregate.MaterialTransferLocations(
                "SITE-001", "LOC-001", "LINE-001", "LINE-LOC-001"),
            now.AddMinutes(1), 1m, "LOT-OP-001");
        var workOrderRequest = Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-WO-001", "WO-MAT", null, "MAT-OIL", "L", 2m, now.AddMinutes(2));
        workOrderRequest.ConfirmAndPostLineSideReceipt(
            new Domain.AggregatesModel.MaterialSupplyAggregate.MaterialTransferLocations(
                "SITE-001", "LOC-001", "LINE-001", "LINE-LOC-001"),
            now.AddMinutes(3), 2m, "LOT-WO-001");
        var otherOperationRequest = Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-OP-002", "WO-MAT", "OP-MAT-20", "MAT-OIL", "L", 3m, now.AddMinutes(4));
        otherOperationRequest.ConfirmAndPostLineSideReceipt(
            new Domain.AggregatesModel.MaterialSupplyAggregate.MaterialTransferLocations(
                "SITE-001", "LOC-001", "LINE-001", "LINE-LOC-001"),
            now.AddMinutes(5), 3m, "LOT-OP-002");
        dbContext.MaterialIssueRequests.AddRange(operationRequest, workOrderRequest, otherOperationRequest);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery(
                "org-001", "env-dev", "WO-MAT", Skip: 0, Take: 10, OperationTaskId: "OP-MAT-10"),
            CancellationToken.None);

        Assert.Equal(2, result.Total);
        Assert.Equal(
            ["MIR-WO-001", "MIR-OP-001"],
            result.Items.Select(item => item.RequestId).ToArray());
        Assert.All(result.Items, item => Assert.Equal(
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.ReceivedStatus,
            item.Status));
    }

    [Fact]
    public async Task Mes_list_queries_apply_server_filters_before_count_and_page()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");

        var targetOrder = WorkOrder.Create("org-001", "env-dev", "WO-FILTER-001", "SKU-FILTER", "PV-001", 1m, 10, now);
        var targetTasks = targetOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-FILTER-10",
                    10,
                    "WC-FILTER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        targetTasks.Single().Assign("operator-001", "DEV-FILTER", "SHIFT-FILTER", now);
        var otherOrder = WorkOrder.Create("org-001", "env-dev", "WO-OTHER-001", "SKU-OTHER", "PV-001", 1m, 10, now.AddMinutes(1));
        var otherTasks = otherOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-OTHER-10",
                    10,
                    "WC-OTHER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        otherTasks.Single().Assign("operator-002", "DEV-OTHER", "SHIFT-OTHER", now);
        dbContext.WorkOrders.AddRange(targetOrder, otherOrder);
        dbContext.OperationTasks.AddRange(targetTasks);
        dbContext.OperationTasks.AddRange(otherTasks);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var workOrders = await new ListMesWorkOrdersQueryHandler(dbContext).Handle(
            new ListMesWorkOrdersQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "filter", WorkCenterId: "WC-FILTER"),
            CancellationToken.None);
        var operationTasks = await new ListOperationTasksQueryHandler(dbContext).Handle(
            new ListOperationTasksQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "DEV-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var dispatchTasks = await new ListDispatchTasksQueryHandler(dbContext).Handle(
            new ListDispatchTasksQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "OP-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var wip = await new GetWipSummaryQueryHandler(dbContext).Handle(
            new GetWipSummaryQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "WO-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);

        Assert.Equal(1, workOrders.Total);
        Assert.Equal("WO-FILTER-001", Assert.Single(workOrders.Items).WorkOrderId);
        Assert.Equal(1, operationTasks.Total);
        var operationTask = Assert.Single(operationTasks.Items);
        Assert.Equal("OP-FILTER-10", operationTask.OperationTaskId);
        Assert.Null(operationTask.WorkOrderNo);
        Assert.Null(operationTask.OperationTaskNo);
        Assert.Equal("WC-FILTER", operationTask.WorkCenterCode);
        Assert.Null(operationTask.WorkCenterName);
        Assert.Null(operationTask.DeviceAssetCode);
        Assert.Null(operationTask.DeviceAssetName);
        Assert.Equal(1, dispatchTasks.Total);
        var dispatchTask = Assert.Single(dispatchTasks.Items);
        Assert.Equal("OP-FILTER-10", dispatchTask.OperationTaskId);
        Assert.Equal("WO-FILTER-001", dispatchTask.WorkOrderNo);
        Assert.Equal("OP-FILTER-10", dispatchTask.OperationTaskNo);
        Assert.Equal("WC-FILTER", dispatchTask.WorkCenterCode);
        Assert.Null(dispatchTask.WorkCenterName);
        Assert.Equal("DEV-FILTER", dispatchTask.DeviceAssetCode);
        Assert.Null(dispatchTask.DeviceAssetName);
        Assert.Equal(1, wip.Total);
        var wipItem = Assert.Single(wip.Items);
        Assert.Equal("OP-FILTER-10", wipItem.OperationTaskId);
        Assert.Equal("WO-FILTER-001", wipItem.WorkOrderNo);
        Assert.Equal("OP-FILTER-10", wipItem.OperationTaskNo);
        Assert.Equal("WC-FILTER", wipItem.WorkCenterCode);
        Assert.Null(wipItem.WorkCenterName);
    }

    [Fact]
    public async Task Mes_secondary_production_lists_apply_keyword_and_structured_filters_before_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");

        var targetOrder = WorkOrder.Create("org-001", "env-dev", "WO-FILTER", "SKU-FILTER", "PV-001", 1m, 10, now);
        var targetTasks = targetOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-FILTER",
                    10,
                    "WC-FILTER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        targetTasks.Single().Assign("operator-001", "DEV-FILTER", "SHIFT-FILTER", now);
        var otherOrder = WorkOrder.Create("org-001", "env-dev", "WO-OTHER", "SKU-OTHER", "PV-001", 1m, 10, now.AddMinutes(1));
        var otherTasks = otherOrder.Release(
            now.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-OTHER",
                    10,
                    "WC-OTHER",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        otherTasks.Single().Assign("operator-002", "DEV-OTHER", "SHIFT-OTHER", now);
        dbContext.WorkOrders.AddRange(targetOrder, otherOrder);
        dbContext.OperationTasks.AddRange(targetTasks);
        dbContext.OperationTasks.AddRange(otherTasks);
        dbContext.ProductionReports.AddRange(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-FILTER", "WO-FILTER", "OP-FILTER", 1m, 0m, false, now),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-OTHER", "WO-OTHER", "OP-OTHER", 1m, 0m, false, now.AddMinutes(1)));
        dbContext.FinishedGoodsReceiptRequests.AddRange(
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-FILTER", "WO-FILTER", "SKU-FILTER", 1m, "PCS", now),
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-OTHER", "WO-OTHER", "SKU-OTHER", 1m, "PCS", now.AddMinutes(1)));
        dbContext.MaterialIssueRequests.AddRange(
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-FILTER", "WO-FILTER", "OP-FILTER", "MAT-FILTER", "PCS", 1m, now),
            Domain.AggregatesModel.MaterialSupplyAggregate.MaterialIssueRequest.Create("org-001", "env-dev", "MIR-OTHER", "WO-OTHER", "OP-OTHER", "MAT-OTHER", "PCS", 1m, now.AddMinutes(1)));
        dbContext.WorkCenterUnavailabilities.AddRange(
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-FILTER", "WC-FILTER", now, null, "filter-reason", "DEV-FILTER"),
            Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open("org-001", "env-dev", "DOWNTIME-OTHER", "WC-OTHER", now.AddMinutes(1), null, "other-reason", "DEV-OTHER"));
        await new CreateShiftHandoverCommandHandler(dbContext).Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-FILTER", "TEAM-FILTER", now, "handover-filter"),
            CancellationToken.None);
        await new CreateShiftHandoverCommandHandler(dbContext).Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-OTHER", "TEAM-OTHER", now.AddMinutes(1), "handover-other"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-FILTER", "OP-FILTER", "DEF-FILTER", 1m, now.AddMinutes(2), "defect-filter"),
            CancellationToken.None);
        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-OTHER", "OP-OTHER", "DEF-OTHER", 1m, now.AddMinutes(3), "defect-other"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reports = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "PRPT-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var receipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "SKU-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var exactReceipt = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", null, RequestNo: "FGR-FILTER"),
            CancellationToken.None);
        var materialIssues = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Keyword: "MAT-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var qualityItems = await new ListRelatedQualityItemsQueryHandler(dbContext).Handle(
            new ListRelatedQualityItemsQuery("org-001", "env-dev", null, null, Skip: 0, Take: 10, Keyword: "DEF-FILTER", WorkCenterId: "WC-FILTER", ShiftId: "SHIFT-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var downtimeEvents = await new ListDowntimeEventsQueryHandler(dbContext, TimeProvider.System).Handle(
            new ListDowntimeEventsQuery(
                "org-001",
                "env-dev",
                "WC-FILTER",
                "DEV-FILTER",
                Skip: 0,
                Take: 10,
                Keyword: "DOWNTIME-FILTER",
                ShiftId: "SHIFT-FILTER",
                WindowStartUtc: now.AddDays(-1),
                WindowEndUtc: now.AddDays(1)),
            CancellationToken.None);
        var capacityImpacts = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", "DEV-FILTER", Skip: 0, Take: 10, WorkCenterId: "WC-FILTER", Keyword: "filter-reason", ShiftId: "SHIFT-FILTER"),
            CancellationToken.None);
        var handovers = await new ListShiftHandoversQueryHandler(dbContext).Handle(
            new ListShiftHandoversQuery("org-001", "env-dev", "SHIFT-FILTER", Skip: 0, Take: 10, Keyword: "TEAM-FILTER", WorkCenterId: "WC-FILTER", DeviceAssetId: "DEV-FILTER"),
            CancellationToken.None);
        var nonMatchingReceipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "posted"),
            CancellationToken.None);
        var nonMatchingMaterialIssues = await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(
            new ListMaterialIssueRequestsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "received"),
            CancellationToken.None);
        var nonMatchingQualityItems = await new ListRelatedQualityItemsQueryHandler(dbContext).Handle(
            new ListRelatedQualityItemsQuery("org-001", "env-dev", null, null, Skip: 0, Take: 10, Status: "reworkPending"),
            CancellationToken.None);
        var nonMatchingDowntimeEvents = await new ListDowntimeEventsQueryHandler(dbContext, TimeProvider.System).Handle(
            new ListDowntimeEventsQuery(
                "org-001",
                "env-dev",
                null,
                null,
                Skip: 0,
                Take: 10,
                Status: "recovered",
                WindowStartUtc: now.AddDays(-1),
                WindowEndUtc: now.AddDays(1)),
            CancellationToken.None);
        var nonMatchingCapacityImpacts = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "recovered"),
            CancellationToken.None);
        var nonMatchingHandovers = await new ListShiftHandoversQueryHandler(dbContext).Handle(
            new ListShiftHandoversQuery("org-001", "env-dev", null, Skip: 0, Take: 10, Status: "accepted"),
            CancellationToken.None);

        Assert.Equal("PRPT-FILTER", Assert.Single(reports.Items).ReportNo);
        Assert.Equal("WO-FILTER", Assert.Single(reports.Items).WorkOrderNo);
        Assert.Equal("OP-FILTER", Assert.Single(reports.Items).OperationTaskNo);
        Assert.Equal(1, reports.Total);
        var receipt = Assert.Single(receipts.Items);
        Assert.Equal("FGR-FILTER", receipt.RequestNo);
        Assert.Equal("WO-FILTER", receipt.WorkOrderNo);
        Assert.Equal("FGR-FILTER", Assert.Single(exactReceipt.Items).RequestNo);
        Assert.Equal(1, exactReceipt.Total);
        Assert.Equal("SKU-FILTER", receipt.SkuCode);
        Assert.Equal(1, receipts.Total);
        var materialIssue = Assert.Single(materialIssues.Items);
        Assert.Equal("MIR-FILTER", materialIssue.RequestId);
        Assert.Equal("WO-FILTER", materialIssue.WorkOrderNo);
        Assert.Equal("OP-FILTER", materialIssue.OperationTaskNo);
        Assert.Equal("MAT-FILTER", materialIssue.MaterialCode);
        Assert.Equal(1, materialIssues.Total);
        Assert.Equal("DEF-FILTER", Assert.Single(qualityItems.Items).DefectCode);
        Assert.Equal(1, qualityItems.Total);
        var downtime = Assert.Single(downtimeEvents.Items);
        Assert.Equal("DOWNTIME-FILTER", downtime.DowntimeEventId);
        Assert.Null(downtime.WorkOrderNo);
        Assert.Null(downtime.OperationTaskNo);
        Assert.Equal("DEV-FILTER", downtime.DeviceAssetCode);
        Assert.Null(downtime.DeviceAssetName);
        Assert.Equal(1, downtimeEvents.Total);
        var capacityImpact = Assert.Single(capacityImpacts.Items);
        Assert.Equal("DOWNTIME-FILTER", capacityImpact.ImpactId);
        Assert.Equal("WC-FILTER", capacityImpact.WorkCenterCode);
        Assert.Null(capacityImpact.WorkCenterName);
        Assert.Equal("DEV-FILTER", capacityImpact.DeviceAssetCode);
        Assert.Null(capacityImpact.DeviceAssetName);
        Assert.Equal(1, capacityImpacts.Total);
        Assert.Equal("SHIFT-FILTER", Assert.Single(handovers.Items).ShiftId);
        Assert.Equal(1, handovers.Total);
        Assert.Equal(0, nonMatchingReceipts.Total);
        Assert.Equal(0, nonMatchingMaterialIssues.Total);
        Assert.Equal(0, nonMatchingQualityItems.Total);
        Assert.Equal(0, nonMatchingDowntimeEvents.Total);
        Assert.Equal(0, nonMatchingCapacityImpacts.Total);
        Assert.Equal(0, nonMatchingHandovers.Total);
    }

    [Fact]
    public async Task Related_quality_items_and_shift_handovers_return_persisted_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QUALITY", "SKU-001", "PV-001", 1m, 10, now));
        dbContext.OperationTasks.AddRange(
            OperationTask.Create("org-001", "env-dev", "WO-QUALITY", "OP-10", OperationTaskLifecycleStatus.Queued, 10, "WC-10", [], now, TimeSpan.FromHours(1), null, null),
            OperationTask.Create("org-001", "env-dev", "WO-QUALITY", "OP-20", OperationTaskLifecycleStatus.Queued, 20, "WC-10", [], now, TimeSpan.FromHours(1), null, null),
            OperationTask.Create("org-001", "env-dev", "WO-QUALITY", "OP-30", OperationTaskLifecycleStatus.Queued, 30, "WC-10", [], now, TimeSpan.FromHours(1), null, null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-QUALITY", "OP-10", "DEF-SURFACE", 1m, now.AddMinutes(1), "defect-001"),
            CancellationToken.None);
        var expectedQualityItem = await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-QUALITY", "OP-20", "DEF-MIX", 2m, now.AddMinutes(2), "defect-002"),
            CancellationToken.None);
        await new RecordDefectCommandHandler(dbContext).Handle(
            new RecordDefectCommand("org-001", "env-dev", "WO-QUALITY", "OP-30", "DEF-PACK", 3m, now.AddMinutes(3), "defect-003"),
            CancellationToken.None);
        var shiftHandoverCommandHandler = new CreateShiftHandoverCommandHandler(dbContext);
        await shiftHandoverCommandHandler.Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-A", "TEAM-A", now.AddMinutes(1), "handover-001"),
            CancellationToken.None);
        var acceptedHandover = await shiftHandoverCommandHandler.Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-A", "TEAM-B", now.AddMinutes(2), "handover-002"),
            CancellationToken.None);
        await shiftHandoverCommandHandler.Handle(
            new CreateShiftHandoverCommand("org-001", "env-dev", "SHIFT-A", "TEAM-C", now.AddMinutes(3), "handover-003"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new AcceptShiftHandoverCommandHandler(dbContext).Handle(
            new AcceptShiftHandoverCommand("org-001", "env-dev", acceptedHandover.ReferenceId, now.AddMinutes(4)),
            CancellationToken.None);
        var repeatedAccept = await new AcceptShiftHandoverCommandHandler(dbContext).Handle(
            new AcceptShiftHandoverCommand("org-001", "env-dev", acceptedHandover.ReferenceId, now.AddMinutes(5)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var qualityItems = await new ListRelatedQualityItemsQueryHandler(dbContext).Handle(
            new ListRelatedQualityItemsQuery("org-001", "env-dev", "WO-QUALITY", null, Skip: 1, Take: 1),
            CancellationToken.None);
        var handovers = await new ListShiftHandoversQueryHandler(dbContext).Handle(
            new ListShiftHandoversQuery("org-001", "env-dev", "SHIFT-A", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, qualityItems.Total);
        var qualityItem = Assert.Single(qualityItems.Items);
        Assert.Equal(acceptedHandover.ReferenceId, Assert.Single(handovers.Items).HandoverId);
        Assert.Equal(expectedQualityItem.ReferenceId, qualityItem.QualityItemId);
        Assert.Equal("Defect", qualityItem.SourceType);
        Assert.Equal("OP-20", qualityItem.SourceDocumentId);
        Assert.Equal("Open", qualityItem.Status);
        Assert.Equal("DEF-MIX", qualityItem.DefectCode);
        Assert.Null(qualityItem.NcrId);
        Assert.Equal(3, handovers.Total);
        Assert.Equal("Accepted", Assert.Single(handovers.Items).HandoverStatus);
        Assert.Equal(now.AddMinutes(4), repeatedAccept.AcceptedAtUtc);
    }

    [Fact]
    public async Task Record_defect_is_idempotent_for_same_payload_and_idempotency_key()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-QUALITY", "SKU-001", "PV-001", 1m, 10, now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordDefectCommandHandler(dbContext);
        var command = new RecordDefectCommand(
            "org-001",
            "env-dev",
            "WO-QUALITY",
            null,
            "DEF-SURFACE",
            1m,
            now.AddMinutes(1),
            "defect-idem-001");

        var firstResult = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var secondResult = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            command with { RecordedAtUtc = now.AddMinutes(2) },
            CancellationToken.None));

        Assert.Equal(firstResult, secondResult);
        Assert.Equal(1, await dbContext.DefectRecords.CountAsync(
            x => x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.WorkOrderId == "WO-QUALITY",
            CancellationToken.None));
    }

    [Fact]
    public async Task Record_downtime_is_idempotent_for_the_same_full_payload_and_conflicts_on_a_changed_work_center()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var startedAtUtc = DateTimeOffset.Parse("2026-08-25T14:30:00Z");
        var handler = new RecordDowntimeEventCommandHandler(dbContext);
        var command = new RecordDowntimeEventCommand(
            "org-001",
            "env-dev",
            "WO-DOWNTIME",
            "OP-10",
            "WC-CNC-01",
            "DEV-01",
            "MECH-FAULT",
            startedAtUtc,
            null,
            "downtime-idem-001");

        var firstResult = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var secondResult = await handler.Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() => handler.Handle(
            command with { WorkCenterId = "WC-CNC-02" },
            CancellationToken.None));

        Assert.Equal(firstResult, secondResult);
        Assert.Equal(startedAtUtc, firstResult.AcceptedAtUtc);
        Assert.Equal(1, await dbContext.WorkCenterUnavailabilities.CountAsync(
            x => x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.WorkCenterId == "WC-CNC-01",
            CancellationToken.None));
    }

    [Theory]
    [InlineData("OP-OTHER")]
    [InlineData("OP-MISSING")]
    public async Task Record_defect_rejects_an_operation_that_is_not_under_the_target_work_order(
        string operationTaskId)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-TARGET", "SKU-001", "PV-001", 1m, 10, now),
            WorkOrder.Create("org-001", "env-dev", "WO-OTHER", "SKU-002", "PV-001", 1m, 10, now));
        dbContext.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-OTHER",
            "OP-OTHER",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            now,
            TimeSpan.FromHours(1),
            null,
            null));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordDefectCommandHandler(dbContext);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new RecordDefectCommand(
                "org-001",
                "env-dev",
                "WO-TARGET",
                operationTaskId,
                "DEF-SURFACE",
                1m,
                now.AddMinutes(1),
                $"defect-invalid-operation-{operationTaskId}"),
            CancellationToken.None));

        Assert.Equal(0, await dbContext.DefectRecords.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Convert_plan_endpoint_rejects_missing_due_utc_instead_of_defaulting_to_now()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");

        var response = await client.PostAsJsonAsync("/api/business/v1/mes/production-plans/SUG-001/work-orders", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            skuId = "SKU-FG-1000",
            productionVersionId = "PV-001",
            plannedQuantity = 12m,
            uomCode = "PCS",
            workCenterId = "WC-MIX-01",
            requestedAtUtc = "2026-06-01T08:00:00Z",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_work_order_endpoint_executes_command_validator_and_rejects_empty_reason()
    {
        // Regression guard for the MES command-validation wiring (AddValidatorsFromAssembly +
        // AddKnownExceptionValidationBehavior in Program.cs). Reason is validated only by
        // CancelWorkOrderCommandValidator — WorkOrderReasonRequest has no FastEndpoints Validator<> — so the
        // success=false envelope here proves the MediatR validation pipeline runs. Without the wiring the command
        // validators are dead and the request would fall through to the handler instead. Validation short-circuits
        // before any database access, so this needs no Postgres.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));
        var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:validation-test");

        var response = await client.PostAsJsonAsync(
            "/api/business/v1/mes/work-orders/WO-VALIDATION/cancel",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                reason = string.Empty,
            });

        // MES uses the plain UseKnownExceptionHandler(), so a KnownException is returned at the SERVICE level as
        // HTTP 200 + a success=false envelope; the BusinessGateway is what maps that success=false to a 400
        // downstream. Lock the service-level contract (200) so a status-code regression fails this test, and assert
        // the envelope carries the "Reason" validation message the command validator produced.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reason", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reverse_production_report_endpoint_propagates_actor_ref_to_command()
    {
        var sender = new CapturingReverseProductionReportSender();
        var endpoint = new ReverseProductionReportEndpoint(sender, TimeProvider.System);
        var request = new ReverseProductionReportRequest(
            "org-001",
            "env-dev",
            "RPT-001",
            "correction",
            DateTimeOffset.Parse("2026-07-12T08:00:00Z"),
            "principal:user-42",
            "reverse-001");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            endpoint.HandleAsync(request, CancellationToken.None));

        Assert.Equal("command captured", exception.Message);
        Assert.NotNull(sender.Command);
        Assert.Equal("principal:user-42", sender.Command.ActorRef);
    }

    [Theory]
    [MemberData(nameof(EndpointTypes))]
    public void Mes_endpoints_route_through_mediator(Type endpointType)
    {
        var parameterTypes = endpointType
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ISender), parameterTypes);
    }

    [Fact]
    public async Task Mes_public_production_queries_return_reports_receipt_requests_and_capacity_impacts()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var reportedAt = DateTimeOffset.Parse("2026-05-24T08:00:00Z");
        var workOrder = Domain.AggregatesModel.WorkOrderAggregate.WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "SKU-FG-1000",
            "PV-001",
            10m,
            1,
            reportedAt.AddHours(8));
        var tasks = workOrder.Release(
            reportedAt.AddHours(-1),
            [
                new Domain.AggregatesModel.WorkOrderAggregate.RoutingStepSnapshot(
                    "OP-10",
                    10,
                    "WC-MIX-01",
                    [],
                    TimeSpan.FromMinutes(30)),
            ]);
        tasks.Single().Start(reportedAt.AddMinutes(-10));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.AddRange(tasks);
        var scrapLots = SeedReceivedMaterialIssue(dbContext, "WO-001", "OP-10", "MIR-PUBLIC-SCRAP", reportedAt.AddMinutes(-5), 1m);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var reportResult = await new RecordProductionReportCommandHandler(dbContext, TestProductionReportOeeDimensionSnapshotProvider.Instance).Handle(
            new RecordProductionReportCommand("org-001", "env-dev", "WO-001", "OP-10", 9m, 1m, true, reportedAt, ConsumedMaterialLots: scrapLots),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var producedLotNo = (await dbContext.ProductionReports.SingleAsync(x => x.ReportNo == reportResult.ReportNo, CancellationToken.None)).ProducedLotNo;
        await new CreateFinishedGoodsReceiptRequestCommandHandler(dbContext).Handle(
            new CreateFinishedGoodsReceiptRequestCommand("org-001", "env-dev", "WO-001", "SKU-FG-1000", 9m, "PCS", reportedAt.AddMinutes(15), 12.34m, ProducedLotNo: producedLotNo),
            CancellationToken.None);
        dbContext.WorkCenterUnavailabilities.Add(Domain.AggregatesModel.ScheduleAggregate.WorkCenterUnavailability.Open(
            "org-001",
            "env-dev",
            "DOWNTIME-001",
            "WC-MIX-01",
            reportedAt,
            null,
            "maintenance",
            "ASSET-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reports = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);
        var receipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", "WO-001"),
            CancellationToken.None);
        var capacity = await new ListCapacityImpactsQueryHandler(dbContext).Handle(
            new ListCapacityImpactsQuery("org-001", "env-dev", "ASSET-001"),
            CancellationToken.None);

        var report = Assert.Single(reports.Items);
        Assert.StartsWith("PRPT-", report.ReportNo, StringComparison.Ordinal);
        Assert.Equal("WO-001", report.WorkOrderId);
        Assert.Equal("OP-10", report.OperationTaskId);
        Assert.Equal(9m, report.GoodQuantity);
        var receipt = Assert.Single(receipts.Items);
        Assert.StartsWith("FGR-", receipt.RequestNo, StringComparison.Ordinal);
        Assert.Equal("SKU-FG-1000", receipt.SkuId);
        Assert.Equal(9m, receipt.Quantity);
        var impact = Assert.Single(capacity.Items);
        Assert.Equal("DOWNTIME-001", impact.ImpactId);
        Assert.Equal("ASSET-001", impact.DeviceAssetId);
        Assert.Equal("WC-MIX-01", impact.WorkCenterId);
        Assert.Null(impact.EffectiveToUtc);
    }

    [Fact]
    public async Task Secondary_mes_production_queries_return_offset_page_and_total_count()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-06-03T08:00:00Z");
        dbContext.ProductionReports.AddRange(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-001", "WO-001", "OP-10", 1m, 0m, false, now.AddMinutes(1)),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-002", "WO-001", "OP-20", 1m, 0m, false, now.AddMinutes(2), reportedBy: "user-emp-020"),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-003", "WO-001", "OP-30", 1m, 0m, false, now.AddMinutes(3)));
        dbContext.FinishedGoodsReceiptRequests.AddRange(
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-001", "WO-001", "SKU-001", 1m, "PCS", now.AddMinutes(1)),
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-002", "WO-001", "SKU-001", 1m, "PCS", now.AddMinutes(2)),
            Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate.FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-003", "WO-001", "SKU-001", 1m, "PCS", now.AddMinutes(3)));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var reports = await new ListProductionReportsQueryHandler(dbContext).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", "WO-001", Skip: 1, Take: 1),
            CancellationToken.None);
        var receipts = await new ListFinishedGoodsReceiptRequestsQueryHandler(dbContext).Handle(
            new ListFinishedGoodsReceiptRequestsQuery("org-001", "env-dev", "WO-001", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(3, reports.Total);
        var pagedReport = Assert.Single(reports.Items);
        Assert.Equal("PRPT-002", pagedReport.ReportNo);
        Assert.Equal("user-emp-020", pagedReport.ReportedBy);
        Assert.Equal(3, receipts.Total);
        Assert.Equal("FGR-002", Assert.Single(receipts.Items).RequestNo);
    }

    [Fact]
    public async Task Production_report_detail_projects_all_consumed_lots_with_tenant_isolation()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var now = DateTimeOffset.Parse("2026-07-12T08:00:00Z");
        dbContext.ProductionReports.AddRange(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-001", "env-dev", "PRPT-DETAIL", "WO-001", "OP-10", 8m, 1m, false, now, reportedBy: "user-emp-010"),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-002", "env-dev", "PRPT-DETAIL", "WO-OTHER", "OP-20", 2m, 0m, false, now));
        dbContext.ProductionReportMaterialConsumptions.AddRange(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReportMaterialConsumption.Record("org-001", "env-dev", "PRPT-DETAIL", "WO-001", "OP-10", "MAT-001", "LOT-B", "KG", 3.5m, "MIR-002"),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReportMaterialConsumption.Record("org-001", "env-dev", "PRPT-DETAIL", "WO-001", "OP-10", "MAT-001", "LOT-A", "KG", 2.5m, "MIR-001"),
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReportMaterialConsumption.Record("org-002", "env-dev", "PRPT-DETAIL", "WO-OTHER", "OP-20", "MAT-OTHER", "LOT-OTHER", "PCS", 1m, "MIR-OTHER"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var detail = await new GetProductionReportQueryHandler(dbContext).Handle(
            new GetProductionReportQuery("org-001", "env-dev", "PRPT-DETAIL"),
            CancellationToken.None);

        Assert.Equal("PRPT-DETAIL", detail.Report.ReportNo);
        // 验收 1（#1948）：报工人必须能从读面查到，否则「每条报工可查到操作人」只落在库里。
        Assert.Equal("user-emp-010", detail.Report.ReportedBy);
        Assert.Equal("WO-001", detail.Report.WorkOrderId);
        Assert.Collection(detail.ConsumedMaterialLots,
            first => Assert.Equal(new ConsumedMaterialLotFact("MAT-001", "LOT-A", 2.5m, "KG", "MIR-001"), first),
            second => Assert.Equal(new ConsumedMaterialLotFact("MAT-001", "LOT-B", 3.5m, "KG", "MIR-002"), second));
    }

    [Fact]
    public async Task Production_report_detail_rejects_missing_or_cross_tenant_report()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        dbContext.ProductionReports.Add(
            Domain.AggregatesModel.ProductionReportAggregate.ProductionReport.Record("org-002", "env-dev", "PRPT-HIDDEN", "WO-OTHER", "OP-20", 1m, 0m, false, DateTimeOffset.Parse("2026-07-12T08:00:00Z")));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetProductionReportQueryHandler(dbContext);
        var missing = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new GetProductionReportQuery("org-001", "env-dev", "PRPT-MISSING"), CancellationToken.None));
        var hidden = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new GetProductionReportQuery("org-001", "env-dev", "PRPT-HIDDEN"), CancellationToken.None));
        Assert.Equal("未找到报工记录，ReportNo = PRPT-MISSING", missing.Message);
        Assert.Equal("未找到报工记录，ReportNo = PRPT-HIDDEN", hidden.Message);
    }

    [Fact]
    public async Task Mes_endpoints_require_internal_service_authentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);

        foreach (var route in MesWriteAuthRoutes)
        {
            var postResponse = await client.PostAsJsonAsync(route, new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                trigger = "Manual",
                workOrderId = "WO-RUSH",
                skuId = "SKU-R",
                productionVersionId = "PV-001",
                quantity = 1,
                dueUtc = DateTimeOffset.Parse("2026-05-22T12:00:00Z"),
                workCenterId = "WC-A",
                durationMinutes = 60
            });

            Assert.True(
                postResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"Expected auth failure for {route} but received {(int)postResponse.StatusCode}.");
        }

        var queryResponse = await client.GetAsync("/api/business/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev");

        Assert.True(
            queryResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected auth failure for MES work-order query but received {(int)queryResponse.StatusCode}.");
    }

    private static readonly string[] MesWriteAuthRoutes =
    [
        "/api/business/v1/mes/schedules/run",
        "/api/business/v1/mes/work-orders/rush",
        "/api/business/v1/mes/work-orders/WO-001/close",
        "/api/business/v1/mes/work-orders/WO-001/hold",
        "/api/business/v1/mes/work-orders/WO-001/cancel",
        "/api/business/v1/mes/quality-holds/WO-001/force-release",
        "/api/business/v1/mes/finished-goods-receipt-requests/FGR-001/inventory-posting/retry",
    ];

    public static IEnumerable<object[]> EndpointTypes()
    {
        return MesEndpointContracts.All.Select(x => new object[] { x.EndpointType });
    }

    private sealed class MaterialReadinessSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = Assert.IsType<GetMaterialReadinessQuery>(request);
            var response = new MesMaterialReadinessResponse(
                "WO-SUB-HTTP",
                "Ready",
                [],
                [new MesMaterialReadinessRow(
                    "MAT-PRIMARY",
                    null,
                    10m,
                    12m,
                    0m,
                    0m,
                    0m,
                    0m,
                    "Ready",
                    SubstituteMaterialIds: ["MAT-ALT-A", "MAT-ALT-B"])]);
            return Task.FromResult((TResponse)(object)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingReverseProductionReportSender : ISender
    {
        public ReverseProductionReportCommand? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Command = Assert.IsType<ReverseProductionReportCommand>(request);
            return Task.FromException<TResponse>(new InvalidOperationException("command captured"));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingAuthorizeStartSender : ISender
    {
        public AuthorizeAndStartOperationTaskCommand? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Command = Assert.IsType<AuthorizeAndStartOperationTaskCommand>(request);
            var response = new MesOperationActionResponse(
                Command.OperationTaskId,
                "InProgress",
                DateTimeOffset.Parse("2026-08-25T01:00:00Z"));
            return Task.FromResult((TResponse)(object)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RealOperationActionSender(ChangeOperationTaskStateCommandHandler handler) : ISender
    {
        private readonly List<DateTimeOffset> observedChangedAtUtc = [];

        public int CallCount { get; private set; }

        /// <summary>
        /// The server-generated timestamps the endpoint stamped on each command, in call order.
        /// </summary>
        public IReadOnlyList<DateTimeOffset> ObservedChangedAtUtc => observedChangedAtUtc;

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var command = Assert.IsType<ChangeOperationTaskStateCommand>(request);
            observedChangedAtUtc.Add(command.ChangedAtUtc);
            var response = await handler.Handle(command, cancellationToken);
            return (TResponse)(object)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class LifecycleConflictSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<TResponse>(
                new MesLifecycleConflictException("pause", nameof(OperationTaskLifecycleStatus.Queued)));
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RoutingSnapshotMissingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromException<TResponse>(
                new MesRoutingSnapshotMissingException("product-engineering:missing-production-version"));
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PreviousOperationIncompleteSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Assert.IsType<ChangeOperationTaskStateCommand>(request);
            return Task.FromException<TResponse>(
                new KnownException("前序工序尚未完成：工序 10、工序 20 等 4 道。"));
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingListOperationTasksSender : ISender
    {
        public ListOperationTasksQuery? Query { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Query = Assert.IsType<ListOperationTasksQuery>(request);
            return Task.FromResult((TResponse)(object)new MesOperationTaskListResponse([], 0));
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingAssignDispatchTaskSender : ISender
    {
        public AssignDispatchTaskCommand? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Command = Assert.IsType<AssignDispatchTaskCommand>(request);
            return Task.FromResult((TResponse)(object)new MesAcceptedResponse(
                "Assigned",
                Command.OperationTaskId,
                DateTimeOffset.Parse("2026-08-25T08:00:00Z")));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ProductionReportWireShapeSender(Guid productionReportId) : ISender
    {
        public RecordProductionReportCommand? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Command = Assert.IsType<RecordProductionReportCommand>(request);
            return Task.FromResult((TResponse)(object)new ProductionReportCommandResult(
                new Domain.AggregatesModel.ProductionReportAggregate.ProductionReportId(productionReportId),
                "PRPT-WIRE-001"));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FinishedGoodsReceiptWireShapeSender(Guid receiptRequestId) : ISender
    {
        public CreateFinishedGoodsReceiptRequestCommand? Command { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Command = Assert.IsType<CreateFinishedGoodsReceiptRequestCommand>(request);
            return Task.FromResult((TResponse)(object)new FinishedGoodsReceiptRequestCommandResult(
                new FinishedGoodsReceiptRequestId(receiptRequestId),
                "FGR-WIRE-001"));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static IReadOnlyCollection<ConsumedMaterialLotInput> SeedReceivedMaterialIssue(
        Infrastructure.ApplicationDbContext dbContext,
        string workOrderId,
        string operationTaskId,
        string requestNo,
        DateTimeOffset requestedAtUtc,
        decimal consumedQuantity)
    {
        var request = MaterialIssueRequest.Create(
            "org-001",
            "env-dev",
            requestNo,
            workOrderId,
            operationTaskId,
            "MAT-SCRAP",
            "PCS",
            10m,
            requestedAtUtc);
        request.ConfirmAndPostLineSideReceipt(MaterialSupplyTestFixtures.Locations, requestedAtUtc.AddMinutes(1), 10m, "LOT-SCRAP");
        request.ClearDomainEvents();
        dbContext.MaterialIssueRequests.Add(request);
        return [new ConsumedMaterialLotInput("MAT-SCRAP", "LOT-SCRAP", consumedQuantity, requestNo)];
    }

    private static IQueryable<Domain.AggregatesModel.OperationTaskAggregate.OperationTask> InvokeOperationTaskEntityQuery(
        Infrastructure.ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status,
        string? keyword,
        string? workCenterId,
        string? shiftId,
        string? deviceAssetId,
        string? assignedUserId = null)
    {
        var method = typeof(GetMesWorkOrderDetailQueryHandler).GetMethod(
            "QueryOperationTaskEntities",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable<Domain.AggregatesModel.OperationTaskAggregate.OperationTask>>(
            method.Invoke(null, [
                dbContext,
                organizationId,
                environmentId,
                workOrderId,
                status,
                keyword,
                workCenterId,
                shiftId,
                deviceAssetId,
                assignedUserId,
                null,
                null,
                null,
                null,
            ]));
    }
}

internal sealed class CapturingRecordDefectSender : ISender
{
    public int CallCount { get; private set; }

    public RecordDefectCommand? LastCommand { get; private set; }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastCommand = Assert.IsType<RecordDefectCommand>(request);
        return Task.FromResult((TResponse)(object)new MesAcceptedResponse(
            "Accepted",
            "DEF-001",
            DateTimeOffset.Parse("2026-08-25T14:30:00Z")));
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        CallCount++;
        return Task.CompletedTask;
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class MaterialIssueHttpSender(Infrastructure.ApplicationDbContext dbContext) : ISender
{
    public async Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        object response = request switch
        {
            CreateMaterialIssueRequestCommand command =>
                await new CreateMaterialIssueRequestCommandHandler(dbContext).Handle(command, cancellationToken),
            ListMaterialIssueRequestsQuery query =>
                await new ListMaterialIssueRequestsQueryHandler(dbContext).Handle(query, cancellationToken),
            GetMaterialIssueRequestQuery query =>
                await new GetMaterialIssueRequestQueryHandler(dbContext).Handle(query, cancellationToken),
            _ => throw new NotSupportedException(request.GetType().Name),
        };
        await dbContext.SaveChangesAsync(cancellationToken);
        return (TResponse)response;
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest => throw new NotSupportedException(request.GetType().Name);

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(request.GetType().Name);

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class CapturingRecordDowntimeSender : ISender
{
    public int CallCount { get; private set; }

    public RecordDowntimeEventCommand? LastCommand { get; private set; }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastCommand = Assert.IsType<RecordDowntimeEventCommand>(request);
        return Task.FromResult((TResponse)(object)new MesAcceptedResponse(
            "Accepted",
            "DOWNTIME-001",
            DateTimeOffset.Parse("2026-08-26T00:00:00Z")));
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        CallCount++;
        return Task.CompletedTask;
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class DistinguishingContextScanSender : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var query = Assert.IsType<PrevalidateContextScanQuery>(request);
        _ = cancellationToken;
        if (query.ScannedObjectId == "worker-001")
        {
            throw new KnownException(
                "WORKER_SKILL_SOURCE_UNAVAILABLE: MasterData 人员资格来源暂不可用。");
        }

        return Task.FromResult((TResponse)(object)new MesContextScanPrevalidationResponse(
            MesContextScanDecision.Rejected,
            "personnel-mismatch",
            query.WorkOrderId,
            query.OperationTaskId,
            query.ObjectType,
            query.ScannedObjectId,
            DateTimeOffset.Parse("2026-08-28T01:00:00Z")));
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class AcceptedMaterialScanSender : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var query = Assert.IsType<PrevalidateMaterialScanQuery>(request);
        _ = cancellationToken;
        return Task.FromResult((TResponse)(object)new MesMaterialScanPrevalidationResponse(
            MesMaterialScanDecision.Accepted,
            "material-scan-accepted",
            query.MaterialIssueRequestId,
            query.WorkOrderId,
            query.OperationTaskId,
            "MAT-SUB",
            "LOT-001",
            "substitute",
            DateTimeOffset.Parse("2026-08-26T08:00:00Z")));
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal static class MesTestProvider
{
    public static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"mes-production-contract-{Guid.NewGuid():N}";
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<Infrastructure.ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }
}

internal sealed class NoopMediator : IMediator
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot send requests.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        throw new NotSupportedException("No-op mediator cannot send requests.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot send requests.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot stream requests.");
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("No-op mediator cannot stream requests.");
    }
}

internal sealed class NoRequirementSnapshotProvider : IMesMaterialRequirementSnapshotProvider
{
    public static readonly NoRequirementSnapshotProvider Instance = new();

    public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
        MesMaterialRequirementSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}
