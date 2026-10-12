using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Wms;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;
using System.Data.Common;
using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class QualityReworkReceiptRedisCapTransportTests
{
    private const string DeploymentProfile = "Issue2809Acceptance";

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Redis_cap_transport_recovers_replayed_receipt_and_preserves_the_first_system_binding_in_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await InitializeAsync(factory);

        var ncrId = await AddOpenNcrAsync(factory);
        var receipt = await BuildReceiptAsync(factory, ncrId, "RW-TRANSPORT-001", "receipt-early");

        await PublishAsync(factory, receipt);
        await AssertEventuallyAsync(factory, async (db, deadLetters, token) =>
        {
            Assert.Null((await db.NonconformanceReports.AsNoTracking().SingleAsync(token)).ReworkWorkOrderId);
            Assert.Contains(
                await deadLetters.ListAsync(ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr.ConsumerName, null, token),
                message => message.FailureCode == "quality.reworkWorkOrderCreated.bindingConflict");
        });

        await SubmitReworkDispositionAsync(factory, ncrId);
        await AssertConcurrentReceiptsCannotOverwriteAsync(factory);
        await PublishAsync(factory, receipt with { EventId = "receipt-replayed" });
        await PublishAsync(factory, receipt with { EventId = "receipt-duplicate" });
        await PublishAsync(factory, receipt with
        {
            EventId = "receipt-conflict",
            Payload = receipt.Payload with { ReworkWorkOrderId = "RW-TRANSPORT-CONFLICT" },
        });
        await PublishAsync(factory, receipt with
        {
            EventId = "receipt-cross-scope",
            OrganizationId = "org-other",
        });

        await AssertEventuallyAsync(factory, async (db, deadLetters, token) =>
        {
            var ncr = await db.NonconformanceReports.AsNoTracking().SingleAsync(x => x.Id == ncrId, token);
            Assert.Equal("RW-TRANSPORT-001", ncr.ReworkWorkOrderId);
            Assert.Equal("created", ncr.ReworkWorkOrderCreationStatus);

            var failures = await deadLetters.ListAsync(
                ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr.ConsumerName,
                null,
                token);
            Assert.Contains(failures, message => message.FailureCode == "quality.reworkWorkOrderCreated.bindingConflict");
            Assert.Contains(failures, message => message.FailureCode == "quality.reworkWorkOrderCreated.ncrNotFoundInScope");

            var receivedCount = await db.Database
                .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cap.received")
                .SingleAsync(token);
            Assert.True(receivedCount >= 5, $"Expected at least five delivered receipts, observed {receivedCount}.");
        });
    }

    // #4356: exercise the original production consumer through the existing HTTP replay entry.
    [QualityReworkReceiptPostgresRedisFact]
    public async Task Dead_letter_http_recovers_original_ncr_binding_and_reports_business_rejection_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var ncrId = await AddOpenNcrAsync(factory);
        var receipt = await BuildReceiptAsync(factory, ncrId, "RW-DLQ-001", "receipt-original-dlq");
        IntegrationEventDeadLetterMessage letter;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr>()
                .HandleAsync(receipt, CancellationToken.None);
            letter = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>()
                .ListAsync(ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr.ConsumerName, null, CancellationToken.None));
            Assert.Equal("quality.reworkWorkOrderCreated.bindingConflict", letter.FailureCode);
        }
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Pending);
        await ReplayStatusAsync(client, letter.Id, "failed");
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Failed);
        await SubmitReworkDispositionAsync(factory, ncrId);
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Replayed);
        IntegrationEventDeadLetterMessage noHandler;
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            noHandler = await store.AddAsync(IntegrationEventDeadLetterMessage.Create("undeclared-quality-consumer", receipt,
                "original-failure", "retained failure"), CancellationToken.None);
            noHandler = (await store.GetAsync(noHandler.Id, CancellationToken.None))!;
            await store.AddAsync(IntegrationEventDeadLetterMessage.Create(letter.ConsumerName,
                receipt with { EventId = "receipt-conflict-dlq", Payload = receipt.Payload with { ReworkWorkOrderId = "RW-CONFLICT" } },
                "original", "original failure"), CancellationToken.None);
            await store.AddAsync(IntegrationEventDeadLetterMessage.Create(letter.ConsumerName,
                receipt with { EventId = "receipt-duplicate-dlq" }, "original", "original failure"), CancellationToken.None);
        }
        using var batch = await client.PostAsJsonAsync("/api/business/v1/quality/dlq/replay-batch", new { status = "pending" });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        using var json = JsonDocument.Parse(await batch.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, items.Length);
        Assert.Equal(2, items.Count(x => x.GetProperty("status").GetString() == "replayed"));
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "noHandler");
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "failed");
        Assert.Contains(items, x => x.GetProperty("status").GetString() == "replayed");
        using var read = factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = Assert.Single(await db.NonconformanceReports.AsNoTracking().ToListAsync());
        Assert.Equal("RW-DLQ-001", ncr.ReworkWorkOrderId);
        var storeRead = read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var saved = await storeRead.GetAsync(letter.Id, CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, saved!.Status);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(letter.EventJson), JsonNode.Parse(saved.EventJson)));
        Assert.Equal(noHandler, await storeRead.GetAsync(noHandler.Id, CancellationToken.None));
    }

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Dead_letter_http_selects_only_original_operation_consumer_and_creates_one_inspection_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var original = new MesOperationTaskCompletedIntegrationEvent("operation-original-dlq", MesIntegrationEventTypes.OperationTaskCompleted, 1,
            at, QualityIntegrationEventSources.BusinessMes, "corr-original", "cause-original", "org-001", "env-dev", "system:mes", "operation-original-key",
            new OperationTaskCompletedPayload("WO-DLQ", "OP-DLQ", "SKU-DLQ", 10, "WC-DLQ", 4m, "ea", true, at));
        IntegrationEventDeadLetterMessage letter;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = InspectionPlan.Create("org-001", "env-dev", "PLAN-DLQ", "operation", "SKU-DLQ", null, "WC-DLQ", null, "operation-task");
            plan.AddCharacteristic("appearance", "Appearance", "visual", "minor", true, "all");
            plan.Activate();
            db.InspectionPlans.Add(plan);
            await db.SaveChangesAsync();
            letter = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                IntegrationEventDeadLetterMessage.Create(MesOperationCompletedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
                    original, "database-unavailable", "original transient database outage"), CancellationToken.None);
        }
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await ReplayStatusAsync(client, letter.Id, "replayed");
        using var read = factory.Services.CreateScope();
        var dbRead = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = Assert.Single(await dbRead.InspectionTasks.AsNoTracking().ToListAsync());
        Assert.Equal("WO-DLQ", task.SourceDocumentId);
        Assert.Equal("OP-DLQ", task.SourceDocumentLineId);
        Assert.Equal(original.IdempotencyKey, task.TriggerIdempotencyKey);
        Assert.Equal(at, task.CreatedAtUtc);
        Assert.Empty(await dbRead.ProcessedIntegrationEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await dbRead.PeriodicInspectionOperations.ToListAsync());
    }

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Spc_consumer_http_commits_inbox_with_one_alert_and_accepts_no_alert_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        InspectionPlan plan;
        InspectionPlan warmupPlan;
        using (var setup = factory.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            plan = InspectionPlan.Create("org-001", "env-dev", "SPC-DLQ", "operation", "SKU-SPC", null, "WC-SPC", null, "mes-operation");
            plan.AddCharacteristic("length", "Length", "caliper", "major", true, "subgroup-2",
                InspectionCharacteristicTypes.Variable, 10m, 9m, 12m, "mm", null);
            plan.Activate();
            db.InspectionPlans.Add(plan);
            for (var i = 0; i < 12; i++)
            {
                var record = InspectionRecord.CreateFromPlan(plan, "operation", "mes-operation", $"SPC-WO-{i}", null,
                    "SKU-SPC", 1m, null, null, null, [InspectionResultLineInput.Measure("length", 10m + i / 10m, "mm", [])], null, []);
                db.InspectionRecords.Add(record);
                db.Entry(record).Property(x => x.CreatedAtUtc).CurrentValue = at.UtcDateTime.AddMinutes(i);
            }
            warmupPlan = InspectionPlan.Create("org-001", "env-dev", "SPC-WARMUP-DLQ", "operation", "SKU-WARMUP", null, "WC-SPC", null, "mes-operation");
            warmupPlan.AddCharacteristic("length", "Length", "caliper", "major", true, "subgroup-2",
                InspectionCharacteristicTypes.Variable, 10m, 9m, 12m, "mm", null);
            warmupPlan.Activate();
            db.InspectionPlans.Add(warmupPlan);
            db.InspectionRecords.Add(InspectionRecord.CreateFromPlan(warmupPlan, "operation", "mes-operation", "SPC-WARMUP-WO", null,
                "SKU-WARMUP", 1m, null, null, null, [InspectionResultLineInput.Measure("length", 10m, "mm", [])], null, []));
            await db.SaveChangesAsync();
        }
        var original = new InspectionResultIntegrationEvent("spc-original-dlq", QualityIntegrationEventTypes.InspectionPassed, 1,
            at, QualityIntegrationEventSources.BusinessQuality, "corr-spc", "cause-spc", "org-001", "env-dev", "system:quality", "spc-original-key",
            new InspectionResultPayload("record-spc", plan.Id.ToString(), "operation", "mes-operation", "SPC-WO", "SKU-SPC", 1m,
                "passed", null, [], at, ResultLines: [new InspectionResultLinePayload("length", 11.1m, "11.1", "mm", "passed", null, null)]));
        using (var scope = factory.Services.CreateScope())
        {
            var unexecuted = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                IntegrationEventDeadLetterMessage.Create(InspectionResultIntegrationEventHandlerForEvaluateSpc.ConsumerName,
                    original with { EventId = "spc-missing-plan", Payload = original.Payload with { InspectionPlanId = Guid.CreateVersion7().ToString() } },
                    "missing-plan", "original missing plan"), CancellationToken.None);
            await ReplayStatusAsync(client, unexecuted.Id, "failed");
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProcessedIntegrationEvents.ToListAsync());
        }
        foreach (var evt in new[] { original, original with { EventId = "spc-no-alert-dlq", IdempotencyKey = "spc-no-alert-key", Payload = original.Payload with { InspectionPlanId = warmupPlan.Id.ToString(), SkuCode = "SKU-WARMUP" } } })
        {
            IntegrationEventDeadLetterMessage letter;
            using (var scope = factory.Services.CreateScope())
                letter = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                    IntegrationEventDeadLetterMessage.Create(InspectionResultIntegrationEventHandlerForEvaluateSpc.ConsumerName,
                        evt, "original-transient-failure", "original evaluation outage"), CancellationToken.None);
            await ReplayStatusAsync(client, letter.Id, "replayed");
            await ReplayStatusAsync(client, letter.Id, "replayed");
            using var read = factory.Services.CreateScope();
            var saved = await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(letter.Id, CancellationToken.None);
            Assert.Equal(evt.EventId, saved!.EventId);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(letter.EventJson), JsonNode.Parse(saved.EventJson)));
        }
        using (var outerScope = factory.Services.CreateScope())
        {
            var unit = outerScope.ServiceProvider.GetRequiredService<ITransactionUnitOfWork>();
            await using var transaction = await unit.BeginTransactionAsync(CancellationToken.None);
            unit.CurrentTransaction = transaction;
            try
            {
                await outerScope.ServiceProvider.GetRequiredService<InspectionResultIntegrationEventHandlerForEvaluateSpc>()
                    .HandleAsync(original with { EventId = "spc-outer-transaction", IdempotencyKey = "spc-outer-key",
                        Payload = original.Payload with { InspectionPlanId = warmupPlan.Id.ToString(), SkuCode = "SKU-WARMUP" } }, CancellationToken.None);
                await unit.CommitAsync(CancellationToken.None);
            }
            finally
            {
                unit.CurrentTransaction = null;
            }
        }
        using var finalRead = factory.Services.CreateScope();
        var finalDb = finalRead.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(3, await finalDb.ProcessedIntegrationEvents.CountAsync());
        var prefix = finalRead.ServiceProvider.GetRequiredService<IOptions<CapOptions>>().Value.TopicNamePrefix;
        var name = string.IsNullOrWhiteSpace(prefix) ? nameof(SpcAlertRaisedIntegrationEvent) : $"{prefix}.{nameof(SpcAlertRaisedIntegrationEvent)}";
        Assert.Equal(1, await finalDb.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM cap.published WHERE "Name" = {name}""").SingleAsync());
    }

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Spc_evaluation_exception_rolls_back_completion_and_outbox_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IRequestHandler<EvaluateSpcControlChartCommand, SpcEvaluationResponse>, FailingSpcEvaluation>()));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        IntegrationEventDeadLetterMessage letter;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = InspectionPlan.Create("org-001", "env-dev", "SPC-FAULT", "operation", "SKU-FAULT", null, "WC-SPC", null, "mes-operation");
            plan.AddCharacteristic("length", "Length", "caliper", "major", true, "subgroup-2",
                InspectionCharacteristicTypes.Variable, 10m, 9m, 12m, "mm", null);
            plan.Activate();
            db.InspectionPlans.Add(plan);
            await db.SaveChangesAsync();
            var original = new InspectionResultIntegrationEvent("spc-fault-original", QualityIntegrationEventTypes.InspectionPassed, 1,
                at, QualityIntegrationEventSources.BusinessQuality, "corr-fault", "cause-fault", "org-001", "env-dev", "system:quality", "spc-fault-key",
                new InspectionResultPayload("fault-record", plan.Id.ToString(), "operation", "mes-operation", "FAULT-WO", "SKU-FAULT", 1m,
                    "passed", null, [], at, ResultLines: [new InspectionResultLinePayload("length", 10m, "10", "mm", "passed", null, null)]));
            letter = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                IntegrationEventDeadLetterMessage.Create(InspectionResultIntegrationEventHandlerForEvaluateSpc.ConsumerName,
                    original, "original-evaluation-outage", "original failure"), CancellationToken.None);
        }
        await ReplayStatusAsync(client, letter.Id, "failed");
        using var read = factory.Services.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await readDb.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(0, await readDb.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cap.published").SingleAsync());
        var saved = await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().GetAsync(letter.Id, CancellationToken.None);
        Assert.Equal(IntegrationEventDeadLetterStatus.Failed, saved!.Status);
        Assert.Contains("controlled-spc-evaluation-failure", saved.FailureMessage, StringComparison.Ordinal);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(letter.EventJson), JsonNode.Parse(saved.EventJson)));
    }

    private sealed class FailingSpcEvaluation : IRequestHandler<EvaluateSpcControlChartCommand, SpcEvaluationResponse>
    {
        public Task<SpcEvaluationResponse> Handle(EvaluateSpcControlChartCommand request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("controlled-spc-evaluation-failure");
    }

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Source_identity_replay_reuses_existing_receiving_task_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var original = new WmsIntegrationEvent("wms-source-first", WmsIntegrationEventTypes.InboundOrderCompleted, 1,
            at, WmsIntegrationEventSources.BusinessWms, "corr-source", "cause-source", "org-001", "env-dev", "system:wms", "first-trigger",
            new WmsIntegrationPayload("IN-DLQ", null, null, null, null, null, null, "completed", null, null,
                Lines: [new WmsIntegrationPayloadLine("LINE-001", "SKU-RM-1000", "ea", "SITE-001", "LOC-001", 2m, "inspection-required")]));
        IntegrationEventDeadLetterMessage letter;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = InspectionPlan.Create("org-001", "env-dev", "RECEIVING-SOURCE", "receiving", "SKU-RM-1000", null, null, null, null);
            plan.AddCharacteristic("appearance", "Appearance", "visual", "minor", true, "all");
            plan.Activate();
            db.InspectionPlans.Add(plan);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks>()
                .HandleAsync(original, CancellationToken.None);
            letter = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                IntegrationEventDeadLetterMessage.Create(WmsInboundOrderCompletedIntegrationEventHandlerForCreateInspectionTasks.ConsumerName,
                    original with { EventId = "wms-source-retry", IdempotencyKey = "retry-trigger" }, "original-outage", "original transient failure"), CancellationToken.None);
        }
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Replayed);
        using var read = factory.Services.CreateScope();
        var task = Assert.Single(await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().InspectionTasks.AsNoTracking().ToListAsync());
        Assert.Equal("first-trigger:LINE-001", task.TriggerIdempotencyKey);
        Assert.Equal("IN-DLQ", task.SourceDocumentId);
        Assert.Equal("LINE-001", task.SourceDocumentLineId);
        var foreign = await read.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
            IntegrationEventDeadLetterMessage.Create(letter.ConsumerName, original with { EventId = "wms-foreign-scope", OrganizationId = "other-org", IdempotencyKey = "foreign-trigger" },
                "original", "original failure"), CancellationToken.None);
        await ReplayStatusAsync(client, foreign.Id, "failed");
        await AssertDeadLetterPersistedAsync(factory, foreign, IntegrationEventDeadLetterStatus.Failed);
        Assert.Equal(1, await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().InspectionTasks.CountAsync());
    }

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Pending_backfill_replay_waits_for_repaired_operation_fact_and_preserves_other_operations_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var completion = new MesOperationTaskCompletedIntegrationEvent("backfill-bad-completion", MesIntegrationEventTypes.OperationTaskCompleted, 1,
            at, MesIntegrationEventSources.BusinessMes, "corr-backfill", "cause-backfill", "org-001", "env-dev", "system:mes", "backfill-completion-key",
            new OperationTaskCompletedPayload("WO-BACKFILL", "OP-10", "WRONG-SKU", 20, "WC-BACKFILL", 1000m, "ea", false, at));
        var original = new WorkOrderReleaseProjectionBackfilledIntegrationEvent("backfill-original", MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled, 1,
            at, MesIntegrationEventSources.BusinessMes, "corr-backfill", "cause-backfill", "org-001", "env-dev", "system:mes", "backfill-original-key",
            new WorkOrderReleasedPayload("WO-BACKFILL", "SKU-BACKFILL", 1000m, at,
                [new ReleasedOperationPayload("OP-10", 10, "WC-BACKFILL"), new ReleasedOperationPayload("OP-20", 20, "WC-BACKFILL")]));
        IntegrationEventDeadLetterMessage letter;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var plan = InspectionPlan.Create("org-001", "env-dev", "BACKFILL-PLAN", "operation", "SKU-BACKFILL", null, "WC-BACKFILL", null, "mes-operation", quantityInterval: 100m);
            plan.AddCharacteristic("appearance", "Appearance", "visual", "minor", true, "all");
            plan.Activate();
            db.InspectionPlans.Add(plan);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection>()
                .HandleAsync(completion, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts>()
                .HandleAsync(original, CancellationToken.None);
            letter = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>()
                .ListAsync(WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName, null, CancellationToken.None));
            Assert.Equal("backfill-operation-rejected", letter.FailureCode);
        }
        await ReplayStatusAsync(client, letter.Id, "failed");
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Failed);
        using (var repair = factory.Services.CreateScope())
        {
            var db = repair.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rejected = await db.PeriodicInspectionOperations.SingleAsync(x => x.OperationId == "OP-10");
            Assert.Null(rejected.ReleasedAtUtc);
            Assert.Empty(await db.PeriodicInspectionRuntimeContexts.Where(x => x.OperationId == "OP-10").ToListAsync());
            Assert.Equal(at.UtcDateTime, (await db.PeriodicInspectionOperations.SingleAsync(x => x.OperationId == "OP-20")).ReleasedAtUtc);
            db.Entry(rejected).Property(x => x.CompletionSkuCode).CurrentValue = "SKU-BACKFILL";
            await db.SaveChangesAsync();
        }
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Replayed);
        using var read = factory.Services.CreateScope();
        var finalDb = read.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.All(await finalDb.PeriodicInspectionOperations.AsNoTracking().ToListAsync(), x => Assert.Equal(at.UtcDateTime, x.ReleasedAtUtc));
        Assert.Equal(2, await finalDb.PeriodicInspectionRuntimeContexts.CountAsync());
        Assert.Equal(20, (await finalDb.PeriodicInspectionOperations.AsNoTracking().SingleAsync(x => x.OperationId == "OP-10")).OperationSequence);
        var notice = Assert.Single(await finalDb.Set<IntegrationEventDeadLetter>().AsNoTracking().Where(x => x.FailureCode == "backfill-release-fact-substituted").ToListAsync());
        Assert.Equal(IntegrationEventDeadLetterStatus.Ignored, notice.Status);
        Assert.Equal(original.EventId, notice.EventId);
        Assert.Single(await finalDb.ProcessedIntegrationEvents.Where(x => x.EventId == original.EventId).ToListAsync());
    }

    [QualityReworkReceiptPostgresRedisFact]
    public async Task Inventory_disposition_replay_accepts_original_case_insensitive_values_on_redis_cap()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        await QualityPostgresLaneDatabase.ResetCapSchemaAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        await InitializeAsync(factory);
        var ncrId = await AddOpenNcrAsync(factory);
        var at = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        IntegrationEventDeadLetterMessage letter;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ncr = await db.NonconformanceReports.SingleAsync(x => x.Id == ncrId);
            ncr.SubmitDisposition(QualityNcrDispositionTypes.ConditionalRelease, "approval-release", ["release-evidence"],
                [MrbReviewInput.Approve("qa-manager", "approved", at)]);
            await db.SaveChangesAsync();
            var original = new StockMovementPostedIntegrationEvent("inventory-uppercase", InventoryIntegrationEventTypes.StockMovementPosted, 1,
                at, InventoryIntegrationEventSources.BusinessInventory, "corr-inventory", "cause-inventory", ncr.OrganizationId, ncr.EnvironmentId, "system:inventory", "inventory-uppercase-key",
                new StockMovementPostedPayload("MOV-RELEASE", "STATUS-TRANSFER-IN", InventoryMovementSourceServices.Quality, ncr.Id.ToString(), null,
                    "inventory-uppercase-key", ncr.SkuCode, "ea", "SITE-001", "LOC-001", ncr.BatchNo, ncr.SerialNo, "RESTRICTED", "company", null, ncr.DefectQuantity, at, null, null));
            letter = await scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>().AddAsync(
                IntegrationEventDeadLetterMessage.Create(StockMovementPostedIntegrationEventHandlerForCompleteQualityNcrInventoryDisposition.ConsumerName,
                    original, "original-outage", "original transient failure"), CancellationToken.None);
        }
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await ReplayStatusAsync(client, letter.Id, "replayed");
        await AssertDeadLetterPersistedAsync(factory, letter, IntegrationEventDeadLetterStatus.Replayed);
        using var read = factory.Services.CreateScope();
        var ncrRead = Assert.Single(await read.ServiceProvider.GetRequiredService<ApplicationDbContext>().NonconformanceReports.AsNoTracking().ToListAsync());
        Assert.Equal("closed", ncrRead.Status);
        Assert.Equal(QualityNcrDispositionTypes.ConditionalRelease, ncrRead.DispositionType);
    }

    private static async Task AssertDeadLetterPersistedAsync(WebApplicationFactory<Program> factory,
        IntegrationEventDeadLetterMessage original, IntegrationEventDeadLetterStatus expected)
    {
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<IntegrationEventDeadLetter>()
            .AsNoTracking().SingleAsync(x => x.Id == original.Id);
        Assert.Equal(expected, row.Status);
        Assert.Equal(original.ConsumerName, row.ConsumerName);
        Assert.Equal(original.EventId, row.EventId);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.EventJson), JsonNode.Parse(row.EventJson)));
    }

    private static async Task ReplayStatusAsync(HttpClient client, Guid id, string expected)
    {
        using var response = await client.PostAsync($"/api/business/v1/quality/dlq/{id}/replay", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "PostgreSQL",
            ["Persistence:AutoMigrate"] = "false",
            ["Quality:Seed:Enabled"] = "false",
            ["ConnectionStrings:PostgreSQL"] = QualityPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "Redis",
            ["Messaging:Redis:ConnectionString"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["ConnectionStrings:Redis"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS"),
            ["Cap:Version"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_VERSION"),
            ["Cap:TopicNamePrefix"] = Environment.GetEnvironmentVariable("NERV_IIP_TEST_CAP_TOPIC_PREFIX"),
            ["InternalService:BearerToken"] = "test-internal-token",
            ["Approval:BaseUrl"] = "https://approval.test",
            ["Erp:BaseUrl"] = "https://erp.test",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DeploymentProfile);
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services => services.PostConfigure<CapOptions>(options =>
            {
                options.SucceedMessageExpiredAfter = 3600;
                options.CollectorCleaningInterval = 3600;
                options.FailedRetryInterval = 1;
            }));
        });
    }

    private static async Task InitializeAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);

        var candidate = Assert.Single(
            scope.ServiceProvider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates(),
            x => x.ImplTypeInfo.AsType() == typeof(ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr));
        var prefix = scope.ServiceProvider.GetRequiredService<IOptions<CapOptions>>().Value.TopicNamePrefix;
        var expectedTopic = string.IsNullOrWhiteSpace(prefix)
            ? nameof(ReworkWorkOrderCreatedIntegrationEvent)
            : $"{prefix}.{nameof(ReworkWorkOrderCreatedIntegrationEvent)}";
        Assert.Equal(expectedTopic, candidate.TopicName);
    }

    private static async Task<NonconformanceReportId> AddOpenNcrAsync(
        WebApplicationFactory<Program> factory,
        string codeSuffix = "001")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = NonconformanceReport.Open(
            "org-transport",
            "env-transport",
            $"NCR-TRANSPORT-{codeSuffix}",
            "in-process",
            "DEF-TRANSPORT-001",
            "SKU-TRANSPORT-001",
            3m,
            "surface-defect",
            "LOT-TRANSPORT-001",
            "SN-TRANSPORT-001",
            []);
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        return ncr.Id;
    }

    private static async Task SubmitReworkDispositionAsync(
        WebApplicationFactory<Program> factory,
        NonconformanceReportId ncrId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ncr = await db.NonconformanceReports.SingleAsync(x => x.Id == ncrId);
        ncr.SubmitDisposition(
            QualityNcrDispositionTypes.Rework,
            "approval-chain-transport",
            [],
            [MrbReviewInput.Approve("qa-manager", "approved", DateTimeOffset.Parse("2026-08-29T10:00:00Z"))]);
        await db.SaveChangesAsync();
    }

    private static async Task<ReworkWorkOrderCreatedIntegrationEvent> BuildReceiptAsync(
        WebApplicationFactory<Program> factory,
        NonconformanceReportId ncrId,
        string reworkWorkOrderId,
        string eventId)
    {
        using var scope = factory.Services.CreateScope();
        var ncr = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .NonconformanceReports.AsNoTracking().SingleAsync(x => x.Id == ncrId);
        return new ReworkWorkOrderCreatedIntegrationEvent(
            eventId,
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
            QualityIntegrationEventSources.BusinessMes,
            "corr-rework-created",
            "evt-rework-requested",
            ncr.OrganizationId,
            ncr.EnvironmentId,
            "system:business-mes",
            $"mes:rework-work-order-created:{ncr.OrganizationId}:{ncr.EnvironmentId}:{ncr.Id}",
            new ReworkWorkOrderCreatedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                reworkWorkOrderId,
                "WO-SOURCE-TRANSPORT",
                "OP-10",
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.BatchNo,
                ncr.SerialNo,
                DateTimeOffset.Parse("2026-08-29T12:00:00Z")));
    }

    private static async Task AssertConcurrentReceiptsCannotOverwriteAsync(
        WebApplicationFactory<Program> factory)
    {
        var ncrId = await AddOpenNcrAsync(factory, "CONCURRENT");
        await SubmitReworkDispositionAsync(factory, ncrId);
        var first = await BuildReceiptAsync(factory, ncrId, "RW-CONCURRENT-A", "receipt-concurrent-a");
        var second = first with
        {
            EventId = "receipt-concurrent-b",
            Payload = first.Payload with { ReworkWorkOrderId = "RW-CONCURRENT-B" },
        };
        var barrier = new BindingUpdateBarrier();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                QualityPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "quality"))
            .AddInterceptors(barrier)
            .Options;
        await using var firstDb = new ApplicationDbContext(options, new NoopMediator());
        await using var secondDb = new ApplicationDbContext(options, new NoopMediator());
        var firstStore = new ReworkWorkOrderBindingStore(
            firstDb,
            new PostgresReworkWorkOrderBindingWriter(firstDb));
        var secondStore = new ReworkWorkOrderBindingStore(
            secondDb,
            new PostgresReworkWorkOrderBindingWriter(secondDb));

        var outcomes = await Task.WhenAll(
            firstStore.BindAsync(first, ncrId, CancellationToken.None),
            secondStore.BindAsync(second, ncrId, CancellationToken.None));

        Assert.Contains(ReworkWorkOrderBindingOutcome.Bound, outcomes);
        Assert.Contains(ReworkWorkOrderBindingOutcome.BindingConflict, outcomes);
        Assert.Equal(2, barrier.Arrivals);
        using var verificationScope = factory.Services.CreateScope();
        var winner = await verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .NonconformanceReports.AsNoTracking()
            .Where(x => x.Id == ncrId)
            .Select(x => x.ReworkWorkOrderId)
            .SingleAsync();
        Assert.Contains(winner, new[] { "RW-CONCURRENT-A", "RW-CONCURRENT-B" });
    }

    private static async Task PublishAsync(
        WebApplicationFactory<Program> factory,
        ReworkWorkOrderCreatedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICapPublisher>()
            .PublishAsync(nameof(ReworkWorkOrderCreatedIntegrationEvent), integrationEvent);
    }

    private static async Task AssertEventuallyAsync(
        WebApplicationFactory<Program> factory,
        Func<ApplicationDbContext, IIntegrationEventDeadLetterStore, CancellationToken, Task> assertion) =>
        await Eventually.AssertAsync(
            condition: "Quality rework receipt CAP delivery converges in PostgreSQL",
            assertion: async token =>
            {
                using var scope = factory.Services.CreateScope();
                await assertion(
                    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>(),
                    token);
            },
            options: new EventuallyOptions(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(250), []));

    private sealed class BindingUpdateBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public int Arrivals => Volatile.Read(ref arrivals);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("UPDATE", StringComparison.Ordinal)
                || !command.CommandText.Contains("nonconformance_reports", StringComparison.Ordinal)
                || !command.CommandText.Contains("rework_work_order_id", StringComparison.Ordinal))
            {
                return result;
            }

            if (Interlocked.Increment(ref arrivals) == 2)
            {
                release.SetResult();
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class QualityReworkReceiptPostgresRedisFactAttribute : FactAttribute
{
    public QualityReworkReceiptPostgresRedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_REDIS")))
            Skip = "Set NERV_IIP_TEST_POSTGRES and NERV_IIP_TEST_REDIS to run the real PostgreSQL + Redis CAP Quality rework-receipt proof.";
    }
}
