using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Testing;
using Npgsql;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// ProviderBehavior / DomainInvariant: #3650。真实 migration、唯一约束及 stale 写入竞争。
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class AndonCallPostgresTests
{
    private static readonly DateTimeOffset RaisedAt = DateTimeOffset.Parse("2026-09-20T01:00:00Z");

    [MesRealPostgresFact]
    public async Task Migration_persists_complete_facts_and_replay_keeps_original_times_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var call = CreateCall();
        call.TryEscalate(RaisedAt.AddMinutes(5), TimeSpan.FromMinutes(5), "supervisor");
        call.Claim("worker", "claim-1", RaisedAt.AddMinutes(7));
        call.Close("worker", "close-1", RaisedAt.AddMinutes(9));
        db.Set<AndonCall>().Add(call);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await db.Set<AndonCall>().SingleAsync();
        saved.Claim("worker", "claim-1", RaisedAt.AddMinutes(20));
        saved.Close("worker", "close-1", RaisedAt.AddMinutes(21));
        Assert.False(saved.TryEscalate(RaisedAt.AddHours(1), TimeSpan.FromMinutes(1), "other"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await db.Set<AndonCall>().SingleAsync();
        Assert.Equal(("org-1", "env-1", "WO-1", "OP-1", "WC-1", "caller"),
            (reloaded.OrganizationId, reloaded.EnvironmentId, reloaded.WorkOrderId, reloaded.OperationTaskIdValue, reloaded.WorkCenterId, reloaded.CallerId));
        Assert.Equal(AndonCallCategory.Equipment, reloaded.Category);
        Assert.Equal(AndonCallStatus.Closed, reloaded.Status);
        Assert.Equal("raise-1", reloaded.RaiseIntentKey);
        Assert.Equal("claim-1", reloaded.ClaimIntentKey);
        Assert.Equal("close-1", reloaded.CloseIntentKey);
        Assert.Equal("worker", reloaded.ResponderId);
        Assert.Equal(RaisedAt, reloaded.RaisedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(7), reloaded.FirstRespondedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(9), reloaded.ClosedAtUtc);
        Assert.Equal(RaisedAt.AddMinutes(5), reloaded.EscalatedAtUtc);
        Assert.Equal("supervisor", reloaded.EscalationRecipientId);
        Assert.Equal(TimeSpan.FromMinutes(7), reloaded.ResponseDuration);
    }

    [MesRealPostgresFact]
    public async Task Stale_concurrent_claims_commit_exactly_one_responder_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var seed = CreateContext();
        await seed.Database.MigrateAsync();
        seed.Set<AndonCall>().Add(CreateCall());
        await seed.SaveChangesAsync();
        await using var first = CreateContext();
        await using var second = CreateContext();
        // 两个上下文都先读取同一未认领版本；即使数据库串行处理 UPDATE，也必须拒绝过期写入。
        var firstCall = await first.Set<AndonCall>().SingleAsync();
        var secondCall = await second.Set<AndonCall>().SingleAsync();
        firstCall.Claim("worker-1", "claim-1", RaisedAt.AddMinutes(1));
        secondCall.Claim("worker-2", "claim-2", RaisedAt.AddMinutes(2));
        var errors = await Task.WhenAll(
            Record.ExceptionAsync(() => first.SaveChangesAsync()),
            Record.ExceptionAsync(() => second.SaveChangesAsync()));
        Assert.Single(errors, error => error is null);
        Assert.IsType<DbUpdateConcurrencyException>(Assert.Single(errors, error => error is not null));
        await using var verification = CreateContext();
        var winner = await verification.Set<AndonCall>().SingleAsync();
        var firstWon = errors[0] is null;
        Assert.Equal(firstWon ? "worker-1" : "worker-2", winner.ResponderId);
        Assert.Equal(firstWon ? "claim-1" : "claim-2", winner.ClaimIntentKey);
        Assert.Equal(RaisedAt.AddMinutes(firstWon ? 1 : 2), winner.FirstRespondedAtUtc);
    }

    [MesRealPostgresFact]
    public async Task Raise_intent_is_unique_within_scope_and_does_not_cross_tenants_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.Set<AndonCall>().Add(CreateCall());
        db.Set<AndonCall>().Add(CreateCall("org-2"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Set<AndonCall>().Add(CreateCall());
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Set<AndonCall>().CountAsync());
        Assert.All(await db.Set<AndonCall>().ToListAsync(), call => Assert.Null(call.ResponseDuration));
    }

    private static AndonCall CreateCall(string organizationId = "org-1") =>
        AndonCall.Raise(organizationId, "env-1", "raise-1", AndonCallCategory.Equipment,
            "WO-1", "OP-1", "WC-1", "caller", RaisedAt);

    private static ApplicationDbContext CreateContext() => new(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // PublicContract / DomainInvariant: #3651。首次响应取持久化时间差，未知结果重放复用同一事实。
    [MesRealPostgresFact]
    public async Task Service_api_preserves_four_category_lifecycles_and_intent_receipts_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var clock = new FakeTimeProvider(RaisedAt);
        await using var factory = CreateApiFactory(clock);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:caller");
        await SeedApiSourceAsync(factory, blocked: true);
        foreach (var category in new[] { "MaterialShortage", "Equipment", "Quality", "Process" })
        {
            var payload = new { organizationId = "org-1", environmentId = "env-1", workOrderId = "WO-1",
                operationTaskId = "OP-1", workCenterId = "WC-1", category, idempotencyKey = $"raise-{category}" };
            using var created = await client.PostAsJsonAsync("/api/business/v1/mes/andon-calls", payload);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var call = await created.Content.ReadFromJsonAsync<JsonElement>();
            var id = call.GetProperty("id").GetString();
            Assert.Equal(JsonValueKind.Null, call.GetProperty("responseDurationSeconds").ValueKind);
            Assert.Equal(JsonValueKind.Null, call.GetProperty("escalatedAtUtc").ValueKind);
            clock.Advance(TimeSpan.FromMinutes(2));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var pending = await db.AndonCalls.SingleAsync(x => x.Id == new AndonCallId(Guid.Parse(id!)));
                Assert.True(pending.TryEscalate(clock.GetUtcNow(), TimeSpan.FromMinutes(1), "supervisor"));
                await db.SaveChangesAsync();
            }
            using var replay = await client.PostAsJsonAsync("/api/business/v1/mes/andon-calls", payload);
            var replayCall = await replay.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(id, replayCall.GetProperty("id").GetString());
            Assert.Equal(call.GetProperty("raisedAtUtc").GetString(), replayCall.GetProperty("raisedAtUtc").GetString());
            Assert.Equal("supervisor", replayCall.GetProperty("escalationRecipientId").GetString());
            var action = new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "claim-1" };
            using var claimed = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{id}/claim", action);
            Assert.Equal(HttpStatusCode.OK, claimed.StatusCode);
            var receipt = await claimed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(120, receipt.GetProperty("responseDurationSeconds").GetDouble());
            clock.Advance(TimeSpan.FromMinutes(1));
            using var claimReplay = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{id}/claim", action);
            Assert.Equal(120, (await claimReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("responseDurationSeconds").GetDouble());
            using var closed = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{id}/close",
                new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "close-1" });
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/business/v1/mes/andon-calls/{id}?organizationId=org-1&environmentId=env-1");
            Assert.Equal("Closed", detail.GetProperty("status").GetString());
            Assert.Equal(120, detail.GetProperty("responseDurationSeconds").GetDouble());
            Assert.Equal(replayCall.GetProperty("escalatedAtUtc").GetString(), detail.GetProperty("escalatedAtUtc").GetString());
            clock.Advance(TimeSpan.FromMinutes(1));
            using var closeReplay = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{id}/close",
                new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "close-1" });
            Assert.Equal(detail.GetProperty("closedAtUtc").GetString(),
                (await closeReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("closedAtUtc").GetString());
        }
        await using var verification = factory.Services.CreateAsyncScope();
        var dbAfter = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(OperationTaskLifecycleStatus.Queued, (await dbAfter.OperationTasks.SingleAsync()).Status);
    }

    [MesRealPostgresFact]
    public async Task Concurrent_api_intents_converge_and_claim_loser_receives_conflict_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var gate = new AndonSaveGate();
        await using var factory = CreateApiFactory(new FakeTimeProvider(RaisedAt), gate);
        using var first = ApiClient(factory, "user:first");
        using var second = ApiClient(factory, "user:second");
        await SeedApiSourceAsync(factory);
        var payload = new { organizationId = "org-1", environmentId = "env-1", workOrderId = "WO-1",
            operationTaskId = "OP-1", workCenterId = "WC-1", category = "Quality", idempotencyKey = "concurrent-raise" };
        gate.Arm();
        var raises = await Task.WhenAll(first.PostAsJsonAsync("/api/business/v1/mes/andon-calls", payload),
            first.PostAsJsonAsync("/api/business/v1/mes/andon-calls", payload));
        Assert.All(raises, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var id = (await raises[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        Assert.Equal(id, (await raises[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString());
        foreach (var response in raises) response.Dispose();
        gate.Arm();
        var claims = await Task.WhenAll(first.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{id}/claim",
                new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "claim-first" }),
            second.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{id}/claim",
                new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "claim-second" }));
        Assert.Single(claims, response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(claims, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("lifecycle-conflict", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<AndonCall>().SingleAsync();
        Assert.Equal(claims[0].IsSuccessStatusCode ? "user:first" : "user:second", saved.ResponderId);
        Assert.Equal(RaisedAt, saved.FirstRespondedAtUtc);
        foreach (var response in claims) response.Dispose();
    }

    [MesRealPostgresFact]
    public async Task Api_rejects_wrong_source_scope_and_actor_and_filters_queue_before_total_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateApiFactory(new FakeTimeProvider(RaisedAt));
        using var client = ApiClient(factory, "user:caller");
        await SeedApiSourceAsync(factory);
        foreach (var bad in new[]
        {
            new { organizationId = "other", environmentId = "env-1", workOrderId = "WO-1", workCenterId = "WC-1", workCenterIds = (string?)null },
            new { organizationId = "org-1", environmentId = "other", workOrderId = "WO-1", workCenterId = "WC-1", workCenterIds = (string?)null },
            new { organizationId = "org-1", environmentId = "env-1", workOrderId = "wrong-pair", workCenterId = "WC-1", workCenterIds = (string?)null },
            new { organizationId = "org-1", environmentId = "env-1", workOrderId = "WO-1", workCenterId = "wrong-center", workCenterIds = (string?)null },
            new { organizationId = "org-1", environmentId = "env-1", workOrderId = "WO-1", workCenterId = "WC-1", workCenterIds = (string?)"WC-2" }
        })
        {
            using var rejected = await client.PostAsJsonAsync("/api/business/v1/mes/andon-calls", new
            { bad.organizationId, bad.environmentId, bad.workOrderId, bad.workCenterId, bad.workCenterIds,
                operationTaskId = "OP-1", category = "Quality", idempotencyKey = "bad-source" });
            Assert.False((await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        }
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var created = await client.PostAsJsonAsync("/api/business/v1/mes/andon-calls", new
            { organizationId = "org-1", environmentId = "env-1", workOrderId = "WO-1", operationTaskId = "OP-1",
                workCenterId = "WC-1", category = "Quality", idempotencyKey = $"queue-{i}" });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            ids.Add((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);
        }
        var firstId = ids[0];
        using var changedPayload = await client.PostAsJsonAsync("/api/business/v1/mes/andon-calls", new
        { organizationId = "org-1", environmentId = "env-1", workOrderId = "WO-1", operationTaskId = "OP-1",
            workCenterId = "WC-1", category = "Equipment", idempotencyKey = "queue-0" });
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
        using var changedPair = await client.PostAsJsonAsync("/api/business/v1/mes/andon-calls", new
        { organizationId = "org-1", environmentId = "env-1", workOrderId = "other-work-order", operationTaskId = "OP-1",
            workCenterId = "WC-1", category = "Quality", idempotencyKey = "queue-0" });
        Assert.Equal(HttpStatusCode.Conflict, changedPair.StatusCode);
        using var claim = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{firstId}/claim",
            new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "claim" });
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        using var other = ApiClient(factory, "user:other");
        using var close = await other.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{firstId}/close",
            new { organizationId = "org-1", environmentId = "env-1", idempotencyKey = "close" });
        Assert.False((await close.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        var root = "/api/business/v1/mes/andon-calls?organizationId=org-1&environmentId=env-1";
        var page = await client.GetFromJsonAsync<JsonElement>(root + "&queue=AwaitingResponse&category=Quality&take=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(ids[1], page.GetProperty("items")[0].GetProperty("id").GetString());
        var nextPage = await client.GetFromJsonAsync<JsonElement>(root + "&queue=AwaitingResponse&take=1&skip=1");
        Assert.Equal(2, nextPage.GetProperty("total").GetInt32());
        Assert.Equal(ids[2], nextPage.GetProperty("items")[0].GetProperty("id").GetString());
        var unclosed = await client.GetFromJsonAsync<JsonElement>(root + "&queue=Unclosed");
        Assert.Equal(3, unclosed.GetProperty("total").GetInt32());
        var outside = await client.GetFromJsonAsync<JsonElement>(root + "&workCenterIds=WC-2");
        Assert.Equal(0, outside.GetProperty("total").GetInt32());
        using var outsideClaim = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{ids[1]}/claim",
            new { organizationId = "org-1", environmentId = "env-1", workCenterIds = "WC-2", idempotencyKey = "outside" });
        Assert.False((await outsideClaim.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        var foreignDetail = await client.GetFromJsonAsync<JsonElement>($"/api/business/v1/mes/andon-calls/{firstId}?organizationId=other&environmentId=env-1");
        Assert.False(foreignDetail.GetProperty("success").GetBoolean());
        var empty = await client.GetFromJsonAsync<JsonElement>(root + "&category=Equipment");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        await using (var rescheduleScope = factory.Services.CreateAsyncScope())
        {
            var db = rescheduleScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var task = await db.OperationTasks.SingleAsync();
            task.ApplyScheduleAssignment("WC-2", null, RaisedAt, RaisedAt.AddHours(1), RaisedAt);
            await db.SaveChangesAsync();
        }
        var movedQueue = await client.GetFromJsonAsync<JsonElement>(root + "&workCenterIds=WC-2");
        Assert.Equal(0, movedQueue.GetProperty("total").GetInt32());
        Assert.Empty(movedQueue.GetProperty("items").EnumerateArray());
        var movedDetail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/business/v1/mes/andon-calls/{ids[1]}?organizationId=org-1&environmentId=env-1&workCenterIds=WC-2");
        Assert.False(movedDetail.GetProperty("success").GetBoolean());
        using var movedClaim = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{ids[1]}/claim",
            new { organizationId = "org-1", environmentId = "env-1", workCenterIds = "WC-2", idempotencyKey = "moved" });
        Assert.False((await movedClaim.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        var authorizedQueue = await client.GetFromJsonAsync<JsonElement>(root + "&workCenterIds=WC-1,WC-2");
        Assert.Equal(3, authorizedQueue.GetProperty("total").GetInt32());
        using var authorizedClaim = await client.PostAsJsonAsync($"/api/business/v1/mes/andon-calls/{ids[1]}/claim",
            new { organizationId = "org-1", environmentId = "env-1", workCenterIds = "WC-1,WC-2", idempotencyKey = "authorized" });
        Assert.Equal("Claimed", (await authorizedClaim.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        using var unauthenticated = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync(root)).StatusCode);
    }

    private static HttpClient ApiClient(WebApplicationFactory<Program> factory, string actor)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", actor);
        return client;
    }

    private static WebApplicationFactory<Program> CreateApiFactory(TimeProvider clock, AndonSaveGate? gate = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = $"test-andon-api-{Guid.CreateVersion7():N}",
                ["InternalService:BearerToken"] = "test-internal-token",
            };
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(clock);
                if (gate is not null) services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(gate));
            });
        });

    private static async Task SeedApiSourceAsync(WebApplicationFactory<Program> factory, bool blocked = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var order = WorkOrder.Create("org-1", "env-1", "WO-1", "SKU-1", "PV-1", 10m, 1, RaisedAt.AddDays(1), "PCS");
        order.MarkReleased();
        db.WorkOrders.Add(order);
        db.OperationTasks.Add(OperationTask.Create("org-1", "env-1", "WO-1", "OP-1", OperationTaskLifecycleStatus.Queued,
            10, "WC-1", [], RaisedAt, TimeSpan.FromHours(1), null, null, "SKU-1"));
        if (blocked)
        {
            order.RecordMaterialRequirementSnapshot(WorkOrder.MaterialRequirementSnapshotCapturedStatus, RaisedAt);
            db.MaterialRequirements.Add(MaterialRequirement.Capture("org-1", "env-1", "WO-1", "OP-1", "MAT-1", null,
                10m, 3m, 0m, "ProductEngineering", "SNAP-1", RaisedAt, []));
            db.QualityHoldContexts.Add(QualityHoldContext.Capture("org-1", "env-1", "WO-1", "OP-1", "Quality",
                "DOC-1", "INSPECTION-1", null, "rejected", "quality.InspectionRejected", "首件不合格", RaisedAt));
            db.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open("org-1", "env-1", "DT-1", "WC-1",
                RaisedAt, null, "equipment-downtime", "ASSET-1"));
        }
        await db.SaveChangesAsync();
        if (blocked)
        {
            var readiness = await new MesOperationTaskActionReadinessEvaluator(db)
                .EvaluateAsync(await db.OperationTasks.SingleAsync(), RaisedAt, CancellationToken.None);
            Assert.DoesNotContain("start", readiness.AllowedActions);
            Assert.Contains(readiness.BlockReasons, reason => reason.Contains("MATERIAL_SHORTAGE", StringComparison.Ordinal));
            Assert.Contains(readiness.BlockReasons, reason => reason.Contains("QUALITY", StringComparison.Ordinal));
            Assert.Contains(readiness.BlockReasons, reason => reason.Contains("equipment.downtime", StringComparison.Ordinal));
        }
    }

    private sealed class AndonSaveGate : SaveChangesInterceptor
    {
        private TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals = 2;
        public void Arm()
        {
            ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            arrivals = 0;
        }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref arrivals) <= 2)
            {
                if (Volatile.Read(ref arrivals) == 2) ready.TrySetResult();
                await TestTimeout.RunAsync("安灯并发保存屏障", async token => await ready.Task.WaitAsync(token), TimeSpan.FromSeconds(15), ct);
            }
            return result;
        }
    }
}
