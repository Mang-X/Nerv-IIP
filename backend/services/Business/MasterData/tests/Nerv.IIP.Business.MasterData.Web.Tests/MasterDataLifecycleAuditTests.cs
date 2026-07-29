using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetCorePal.Extensions.DistributedTransactions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataLifecycleAuditTests
{
    [Theory]
    [MemberData(nameof(SupportedLifecycleIdentities))]
    public void SupportedLifecycleResource_UsesCanonicalAuditIdentity(string resourceType, string code, string? codeSet, DateOnly? effectiveFrom, string expected)
    {
        var command = new SetMasterDataResourceEnabledCommand("org", "env", resourceType, code, false, "actor", "operation", codeSet, "reason", effectiveFrom);
        Assert.Equal(expected, SetMasterDataResourceEnabledCommandHandler.LifecycleIdentity(command, resourceType));
    }

    public static TheoryData<string, string, string?, DateOnly?, string> SupportedLifecycleIdentities => new()
    {
        { "sku", "SKU-1", null, null, "SKU-1" },
        { "unit-of-measure", "kg", null, null, "kg" },
        { "uom-conversion", "kg->g", null, new DateOnly(2026, 1, 2), "kg->g:2026-01-02" },
        { "business-partner", "BP-1", null, null, "BP-1" },
        { "site", "SITE-1", null, null, "SITE-1" },
        { "workshop", "WS-1", null, null, "WS-1" },
        { "department", "D-1", null, null, "D-1" },
        { "team", "T-1", null, null, "T-1" },
        { "shift", "S-1", null, null, "S-1" },
        { "work-calendar", "C-1", null, null, "C-1" },
        { "production-line", "L-1", null, null, "L-1" },
        { "work-center", "WC-1", null, null, "WC-1" },
        { "device-asset", "DEV-1", null, null, "DEV-1" },
        { "reference-data", "active", "lifecycle-status", null, "lifecycle-status:active" },
    };

    [Fact]
    public async Task ReplayOldDisableOperationAfterEnable_DoesNotMutateCurrentState()
    {
        await using var provider = CreateProvider($"masterdata-old-operation-{Guid.NewGuid():N}");
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-REPLAY", "Replay SKU", "pcs", "finished-goods"));
        await db.SaveChangesAsync();
        var handler = new SetMasterDataResourceEnabledCommandHandler(db);
        var disable = new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-REPLAY", false, "user:1", "K1", Reason: "obsolete");
        var enable = new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-REPLAY", true, "user:1", "K2", Reason: "restored");
        await handler.Handle(disable, CancellationToken.None);
        await db.SaveChangesAsync();
        await handler.Handle(enable, CancellationToken.None);
        await db.SaveChangesAsync();

        await handler.Handle(disable, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.False((await db.Skus.SingleAsync(x => x.Code == "SKU-REPLAY")).Disabled);
        Assert.Equal(2, await db.LifecycleAuditEntries.CountAsync());
    }

    [Theory]
    [InlineData("sku", "SKU-NOOP")]
    [InlineData("site", "SITE-NOOP")]
    public async Task FirstNoOpOperation_IsDurablyConsumedAndCannotMutateAfterStateReversal(string resourceType, string code)
    {
        await using var provider = CreateProvider($"masterdata-noop-operation-{Guid.NewGuid():N}");
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (resourceType == "sku")
        {
            db.Skus.Add(Sku.Create("org", "env", code, "No-op SKU", "pcs", "finished-goods"));
        }
        else
        {
            db.Sites.Add(Domain.AggregatesModel.SiteAggregate.Site.Create("org", "env", code, "No-op site", "UTC"));
        }
        await db.SaveChangesAsync();
        var handler = new SetMasterDataResourceEnabledCommandHandler(db);
        var firstNoOp = new SetMasterDataResourceEnabledCommand("org", "env", resourceType, code, true, "user:trusted", "K-NOOP", Reason: "confirm active");

        await handler.Handle(firstNoOp, CancellationToken.None);
        await db.SaveChangesAsync();
        await handler.Handle(firstNoOp with { Enabled = false, OperationId = "K-DISABLE", Reason = "retired" }, CancellationToken.None);
        await db.SaveChangesAsync();
        await handler.Handle(firstNoOp, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.LifecycleAuditEntries.CountAsync());
        Assert.True(resourceType == "sku"
            ? (await db.Skus.SingleAsync(x => x.Code == code)).Disabled
            : (await db.Sites.SingleAsync(x => x.Code == code)).Disabled);
    }

    [Fact]
    public async Task ReusingOperationForDifferentResourceOrPayload_ThrowsKnownConflictBeforeMutation()
    {
        await using var provider = CreateProvider($"masterdata-operation-conflict-{Guid.NewGuid():N}");
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Skus.AddRange(
            Sku.Create("org-001", "env-dev", "SKU-A", "SKU A", "pcs", "finished-goods"),
            Sku.Create("org-001", "env-dev", "SKU-B", "SKU B", "pcs", "finished-goods"));
        await db.SaveChangesAsync();
        var handler = new SetMasterDataResourceEnabledCommandHandler(db);
        await handler.Handle(new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-A", false, "user:1", "K1", Reason: "obsolete"), CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-B", false, "user:1", "K1", Reason: "obsolete"), CancellationToken.None));
        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() => handler.Handle(
            new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-A", false, "user:1", "K1", Reason: "different reason"), CancellationToken.None));

        Assert.False((await db.Skus.SingleAsync(x => x.Code == "SKU-B")).Disabled);
    }

    [Fact]
    public async Task SqliteRelationalReplay_PreflightsOperationBeforeMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-SQLITE", "SQLite SKU", "pcs", "finished-goods"));
        await db.SaveChangesAsync();
        var handler = new SetMasterDataResourceEnabledCommandHandler(db);
        var disable = new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-SQLITE", false, "user:1", "K-SQL", Reason: "obsolete");
        await handler.Handle(disable, CancellationToken.None);
        await db.SaveChangesAsync();
        await handler.Handle(new SetMasterDataResourceEnabledCommand("org-001", "env-dev", "sku", "SKU-SQLITE", true, "user:1", "K-ENABLE", Reason: "restored"), CancellationToken.None);
        await db.SaveChangesAsync();

        await handler.Handle(disable, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.False((await db.Skus.AsNoTracking().SingleAsync()).Disabled);
        Assert.Equal(2, await db.LifecycleAuditEntries.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task UomConversionIdentity_NormalizesCodeAndResolvesLatestEffectiveVersion()
    {
        await using var provider = CreateProvider($"masterdata-uom-identity-{Guid.NewGuid():N}");
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UomConversions.AddRange(
            UomConversion.Create("org", "env", "kg", "g", 1000, 0, 3, "half-up", new DateOnly(2025, 1, 1)),
            UomConversion.Create("org", "env", "kg", "g", 1000, 0, 3, "half-up", new DateOnly(2026, 1, 1)));
        await db.SaveChangesAsync();
        var handler = new SetMasterDataResourceEnabledCommandHandler(db);
        var spaced = new SetMasterDataResourceEnabledCommand("org", "env", "uom-conversion", "kg -> g", false, "actor", "K-UOM", Reason: "superseded");
        await handler.Handle(spaced, CancellationToken.None);
        await db.SaveChangesAsync();

        var compactReplay = spaced with { Code = "kg->g" };
        await handler.Handle(compactReplay, CancellationToken.None);
        await db.SaveChangesAsync();

        var audit = await db.LifecycleAuditEntries.SingleAsync();
        Assert.Equal("kg->g:2026-01-01", audit.ResourceIdentity);
        Assert.True((await db.UomConversions.SingleAsync(x => x.EffectiveFrom == new DateOnly(2026, 1, 1))).Disabled);
        Assert.False((await db.UomConversions.SingleAsync(x => x.EffectiveFrom == new DateOnly(2025, 1, 1))).Disabled);
    }

    [Fact]
    public async Task SqliteConcurrentSameOperation_LoserSucceedsOrReturnsGovernedConflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await seed.Database.EnsureCreatedAsync();
            seed.Skus.AddRange(
                Sku.Create("org", "env", "SKU-RACE", "Race", "pcs", "finished-goods"),
                Sku.Create("org", "env", "SKU-COLLISION", "Collision", "pcs", "finished-goods"));
            await seed.SaveChangesAsync();
        }
        using var winnerScope = provider.CreateScope();
        using var loserScope = provider.CreateScope();
        var winner = winnerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var loser = loserScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var command = new SetMasterDataResourceEnabledCommand("org", "env", "sku", "SKU-RACE", false, "actor", "K-RACE", Reason: "obsolete");
        await new SetMasterDataResourceEnabledCommandHandler(winner).Handle(command, CancellationToken.None);
        await new SetMasterDataResourceEnabledCommandHandler(loser).Handle(command, CancellationToken.None);
        await winner.SaveChangesAsync();

        await loser.SaveChangesAsync();

        using var collisionWinnerScope = provider.CreateScope();
        using var collisionLoserScope = provider.CreateScope();
        var collisionWinner = collisionWinnerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var collisionLoser = collisionLoserScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var collisionCommand = command with { Code = "SKU-COLLISION", OperationId = "K-COLLISION", Reason = "winner reason" };
        await new SetMasterDataResourceEnabledCommandHandler(collisionWinner).Handle(collisionCommand, CancellationToken.None);
        await new SetMasterDataResourceEnabledCommandHandler(collisionLoser).Handle(collisionCommand with { Reason = "different reason" }, CancellationToken.None);
        await collisionWinner.SaveChangesAsync();
        await Assert.ThrowsAsync<NetCorePal.Extensions.Primitives.KnownException>(() => collisionLoser.SaveChangesAsync());
        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await observer.LifecycleAuditEntries.AsNoTracking().CountAsync());
        Assert.True((await observer.Skus.AsNoTracking().SingleAsync(x => x.Code == "SKU-RACE")).Disabled);
        Assert.True((await observer.Skus.AsNoTracking().SingleAsync(x => x.Code == "SKU-COLLISION")).Disabled);
    }

    [Fact]
    public async Task SqliteConcurrentDifferentOperations_WithSameDesiredState_PersistBothFactsWithoutLostState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await seed.Database.EnsureCreatedAsync();
            seed.Skus.Add(Sku.Create("org", "env", "SKU-DIFFERENT-OPS", "Different ops", "pcs", "finished-goods"));
            await seed.SaveChangesAsync();
        }
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var command = new SetMasterDataResourceEnabledCommand("org", "env", "sku", "SKU-DIFFERENT-OPS", false, "actor", "K-FIRST", Reason: "obsolete");
        await new SetMasterDataResourceEnabledCommandHandler(first).Handle(command, CancellationToken.None);
        await new SetMasterDataResourceEnabledCommandHandler(second).Handle(command with { OperationId = "K-SECOND" }, CancellationToken.None);
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await observer.Skus.AsNoTracking().SingleAsync()).Disabled);
        Assert.Equal(2, await observer.LifecycleAuditEntries.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task DisableEndpoint_PersistsAuthenticatedActorAndReloadsAuditWithoutTrustingBodyActor()
    {
        await using var factory = new MasterDataLiveHttpTestFactory();
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-ENDPOINT", "Endpoint SKU", "pcs", "finished-goods"));
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "audit-test-token");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-disable-endpoint");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-disable-endpoint");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:admin-001");
        var body = new { organizationId = "org-001", environmentId = "env-dev", resourceType = "sku", code = "SKU-ENDPOINT", reason = " retired ", actorId = "user:spoofed" };

        var first = await client.PostAsJsonAsync("/api/business/v1/master-data/resources/sku/SKU-ENDPOINT/disable", body);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var replay = await client.PostAsJsonAsync("/api/business/v1/master-data/resources/sku/SKU-ENDPOINT/disable", body);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("X-Idempotency-Key");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-disable-endpoint-new-operation");
        var alreadyDisabled = await client.PostAsJsonAsync("/api/business/v1/master-data/resources/sku/SKU-ENDPOINT/disable", body);
        Assert.True(alreadyDisabled.IsSuccessStatusCode, await alreadyDisabled.Content.ReadAsStringAsync());

        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await observer.Skus.AsNoTracking().SingleAsync(x => x.Code == "SKU-ENDPOINT")).Disabled);
        var audits = await observer.LifecycleAuditEntries.AsNoTracking().OrderBy(x => x.OccurredAtUtc).ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal("user:admin-001", audit.ActorId));
        Assert.Equal("idem-disable-endpoint", audits[0].OperationId);
        Assert.Equal("idem-disable-endpoint-new-operation", audits[1].OperationId);
        Assert.All(audits, audit => Assert.Equal("retired", audit.Reason));
    }

    [Fact]
    public async Task TeamMemberEndpoints_PersistAuthenticatedScopeCandidateAudit()
    {
        await using var factory = new MasterDataLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "audit-test-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:supervisor-001");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-team-member-add");
        var body = new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            teamCode = "TEAM-001",
            userId = "worker-001",
            isLeader = true,
            effectiveFrom = new DateOnly(2026, 7, 28),
            effectiveTo = (DateOnly?)null,
        };

        var added = await client.PostAsJsonAsync("/api/business/v1/master-data/teams/TEAM-001/members", body);
        Assert.True(added.IsSuccessStatusCode, await added.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Remove("X-Idempotency-Key");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-team-member-remove");
        var removed = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/business/v1/master-data/teams/TEAM-001/members/worker-001")
        {
            Content = JsonContent.Create(new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                teamCode = "TEAM-001",
                userId = "worker-001",
                reason = "transferred",
            }),
        });
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync());

        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await observer.ScopeContextAuditEntries.AsNoTracking().OrderBy(x => x.OccurredAtUtc).ToArrayAsync();

        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal("team-member", audit.ResourceType));
        Assert.All(audits, audit => Assert.Equal("TEAM-001:worker-001", audit.ResourceCode));
        Assert.All(audits, audit => Assert.Equal("user:supervisor-001", audit.ActorId));
        Assert.Equal("team-member-assigned", audits[0].OperationKind);
        Assert.Equal("null", audits[0].BeforeJson);
        Assert.Equal("scope-candidate-assigned", audits[0].Reason);
        Assert.Equal("idem-team-member-add", audits[0].OperationId);
        Assert.Equal("TEAM-001:worker-001", audits[0].ResourceIdentity);
        Assert.Contains("\"isLeader\":true", audits[0].AfterJson, StringComparison.Ordinal);
        Assert.Equal("team-member-removed", audits[1].OperationKind);
        Assert.Contains("\"disabled\":true", audits[1].AfterJson, StringComparison.Ordinal);
        Assert.Equal("transferred", audits[1].Reason);
        Assert.Equal("idem-team-member-remove", audits[1].OperationId);
    }

    [Fact]
    public async Task WorkerCreateAndEmploymentStatusUpdate_PersistScopeAvailabilityAudit()
    {
        await using var factory = new MasterDataLiveHttpTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "audit-test-token");
        client.DefaultRequestHeaders.Add("X-Authenticated-Actor", "user:hr-001");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-worker-create");
        var created = await client.PostAsJsonAsync("/api/business/v1/master-data/workers", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            code = "EMP-AUDIT",
            name = "审计员工",
            userId = "user-worker-audit",
            departmentCode = "DEPT-001",
            jobTitle = "操作工",
            employmentStatus = "active",
            phone = (string?)null,
            idempotencyKey = "worker-create-body",
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Remove("X-Idempotency-Key");
        client.DefaultRequestHeaders.Add("X-Idempotency-Key", "idem-worker-status");
        var updated = await client.PatchAsJsonAsync("/api/business/v1/master-data/resources/worker/EMP-AUDIT", new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            resourceType = "worker",
            code = "EMP-AUDIT",
            employmentStatus = "on-leave",
        });
        Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());

        using var observerScope = factory.Services.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audits = await observer.ScopeContextAuditEntries.AsNoTracking().OrderBy(x => x.OccurredAtUtc).ToArrayAsync();

        Assert.Equal(2, audits.Length);
        Assert.Equal("worker-created", audits[0].OperationKind);
        Assert.Equal("idem-worker-create", audits[0].OperationId);
        Assert.Equal("null", audits[0].BeforeJson);
        Assert.Contains("\"userId\":\"user-worker-audit\"", audits[0].AfterJson, StringComparison.Ordinal);
        Assert.Equal("worker-scope-availability-updated", audits[1].OperationKind);
        Assert.Equal("idem-worker-status", audits[1].OperationId);
        Assert.Contains("\"employmentStatus\":\"active\"", audits[1].BeforeJson, StringComparison.Ordinal);
        Assert.Contains("\"employmentStatus\":\"on-leave\"", audits[1].AfterJson, StringComparison.Ordinal);
        Assert.All(audits, audit => Assert.Equal("user:hr-001", audit.ActorId));
    }

    [Fact]
    public async Task ScopeLineageUpdates_PersistCanonicalBeforeAndAfterAudit()
    {
        var databaseName = $"master-data-scope-context-audit-{Guid.NewGuid():N}";
        await using var provider = CreateProvider(databaseName);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Workshops.Add(Workshop.Create("org-001", "env-dev", "WS-001", "一车间", "SITE-001", null, null));
        db.Teams.Add(Team.Create("org-001", "env-dev", "TEAM-001", "甲班", "DEPT-001", "SHIFT-001", "WS-001"));
        db.ProductionLines.Add(ProductionLine.Create("org-001", "env-dev", "LINE-001", "一号线", "SITE-001", "WS-001"));
        db.WorkCenters.Add(WorkCenter.CreateResource(
            "org-001",
            "env-dev",
            "WC-001",
            "一号工作中心",
            480,
            "work-center",
            "SITE-001",
            "LINE-001",
            "WS-001",
            "CAL-001",
            "minute",
            true));
        await db.SaveChangesAsync();

        var handler = new UpdateMasterDataResourceCommandHandler(db, new ReferenceDataCodeRepository(db));
        await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "workshop",
                "WS-001",
                SiteCode: "SITE-002",
                AuditContext: new("corr-workshop", "cause-workshop", "user:supervisor-001", "op-workshop")),
            CancellationToken.None);
        await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "team",
                "TEAM-001",
                ShiftCode: "SHIFT-002",
                WorkshopCode: "WS-002",
                AuditContext: new("corr-team", "cause-team", "user:supervisor-001", "op-team")),
            CancellationToken.None);
        await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "production-line",
                "LINE-001",
                SiteCode: "SITE-002",
                WorkshopCode: "WS-002",
                AuditContext: new("corr-line", "cause-line", "user:supervisor-001", "op-line")),
            CancellationToken.None);
        await handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "work-center",
                "WC-001",
                PlantCode: "SITE-002",
                LineCode: "LINE-002",
                WorkshopCode: "WS-002",
                AuditContext: new("corr-work-center", "cause-work-center", "user:supervisor-001", "op-work-center")),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var audits = await db.ScopeContextAuditEntries.AsNoTracking().OrderBy(x => x.ResourceType).ToArrayAsync();
        Assert.Equal(4, audits.Length);
        Assert.All(audits, audit => Assert.Equal("user:supervisor-001", audit.ActorId));
        Assert.All(audits, audit => Assert.Equal("scope-lineage-updated", audit.Reason));
        Assert.Contains(audits, audit =>
            audit.ResourceType == "workshop"
            && audit.BeforeJson.Contains("\"siteCode\":\"SITE-001\"", StringComparison.Ordinal)
            && audit.AfterJson.Contains("\"siteCode\":\"SITE-002\"", StringComparison.Ordinal));
        Assert.Contains(audits, audit =>
            audit.ResourceType == "team"
            && audit.BeforeJson.Contains("\"workshopCode\":\"WS-001\"", StringComparison.Ordinal)
            && audit.AfterJson.Contains("\"workshopCode\":\"WS-002\"", StringComparison.Ordinal));
        Assert.Contains(audits, audit =>
            audit.ResourceType == "production-line"
            && audit.BeforeJson.Contains("\"siteCode\":\"SITE-001\"", StringComparison.Ordinal)
            && audit.AfterJson.Contains("\"workshopCode\":\"WS-002\"", StringComparison.Ordinal));
        Assert.Contains(audits, audit =>
            audit.ResourceType == "work-center"
            && audit.BeforeJson.Contains("\"plantCode\":\"SITE-001\"", StringComparison.Ordinal)
            && audit.AfterJson.Contains("\"lineCode\":\"LINE-002\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisableSku_PersistsTrustedAuditAndReplayDoesNotDuplicate()
    {
        var databaseName = $"master-data-lifecycle-audit-{Guid.NewGuid():N}";
        await using var provider = CreateProvider(databaseName);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-001", "Audited SKU", "pcs", "finished-goods"));
            await db.SaveChangesAsync();

            var handler = new SetMasterDataResourceEnabledCommandHandler(db);
            var command = new SetMasterDataResourceEnabledCommand(
                "org-001", "env-dev", "sku", "SKU-001", false, "gateway:user-42", "corr-disable-001",
                Reason: "  obsolete specification  ");
            await handler.Handle(command, CancellationToken.None);
            await db.SaveChangesAsync();

            await handler.Handle(command, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sku = await observer.Skus.SingleAsync(x => x.Code == "SKU-001");
        var audit = await observer.LifecycleAuditEntries.SingleAsync();

        Assert.True(sku.Disabled);
        Assert.Equal("org-001", audit.OrganizationId);
        Assert.Equal("env-dev", audit.EnvironmentId);
        Assert.Equal("sku", audit.ResourceType);
        Assert.Equal(sku.Id.ToString(), audit.ResourceId);
        Assert.Equal("SKU-001", audit.ResourceCode);
        Assert.Equal("gateway:user-42", audit.ActorId);
        Assert.Equal("obsolete specification", audit.Reason);
        Assert.Equal("corr-disable-001", audit.OperationId);
        Assert.True(audit.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private static ServiceProvider CreateProvider(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private sealed class MasterDataLiveHttpTestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = $"masterdata-live-audit-{Guid.NewGuid():N}";
        private readonly ServiceProvider efServices = new ServiceCollection().AddEntityFrameworkInMemoryDatabase().BuildServiceProvider();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("InternalService:BearerToken", "audit-test-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IIntegrationEventPublisher, NoopIntegrationEventPublisher>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .UseInternalServiceProvider(efServices)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) efServices.Dispose();
        }
    }

    private sealed class NoopIntegrationEventPublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
