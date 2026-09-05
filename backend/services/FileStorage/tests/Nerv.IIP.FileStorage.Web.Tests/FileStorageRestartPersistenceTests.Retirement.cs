using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.Testing;
using Npgsql;
using static Nerv.IIP.FileStorage.Web.Tests.TemplateAssetRetirementProofTests;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageRestartPersistenceTests
{
    private const string RetirementRoute = "/internal/file-storage/v1/template-asset-retirements";

    [FileStorageRealPostgresFact]
    public async Task Retirement_acceptance_replays_frozen_horizon_and_holds_content_and_legacy_gc_on_postgres()
    {
        await ResetFileStorageSchemaAsync();
        var clock = new FakeTimeProvider(Epoch);
        var root = Directory.CreateTempSubdirectory("nerv-3044-retirement-");
        try
        {
            RetireTemplateAssetResponse accepted;
            await using (var factory = RetirementFactory(clock, root.FullName))
            {
                using var client = CreateClient(factory);
                await SeedRetirementAssetAsync(factory);
                using var scope = factory.Services.CreateScope();
                var files = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                var oldGrant = await files.CreateDownloadGrantAsync("retirement-file", new("retirement-org", "retirement-env"), default);
                Assert.Equal(12, (await files.GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default)).Value!.UsedBytes);
                using var response = await client.PostAsJsonAsync(RetirementRoute, Sign(Fields()));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                accepted = (await response.Content.ReadFromJsonAsync<RetireTemplateAssetResponse>())!;
                Assert.Equal(2592000, accepted.ReplayHorizonSeconds);
                Assert.Equal(Epoch, accepted.QuotaReleasedAtUtc);
                Assert.Equal("physical-hold", accepted.Status);
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ChangeTracker.Clear();
                Assert.Equal(0, (await files.GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default)).Value!.UsedBytes);
                var newGrant = await files.CreateDownloadGrantAsync("retirement-file", new("retirement-org", "retirement-env"), default);
                foreach (var grant in new[] { oldGrant.Value!, newGrant.Value! })
                {
                    var grantId = grant.Download.Url.Split('/')[5];
                    Assert.Null(await ((ILocalFileContentIndex)files).GetUploadSessionIdForDownloadGrantAsync(
                        grantId, "retirement-org", "retirement-env", default));
                }
                // Same decision cannot change its frozen upstream policy, file or ownership facts.
                foreach (var index in new[] { 8, 9, 10, 11, 12, 13, 14, 17 })
                {
                    var changed = Fields();
                    changed[index] = index is 8 or 9 or 10 ? "600" : index == 14 ? $"sha256:{new string('a', 64)}" : "different";
                    using var conflict = await client.PostAsJsonAsync(RetirementRoute, Sign(changed));
                    Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                }
                var otherDecision = Fields();
                otherDecision[6] = "01991000-0000-7000-8000-000000003045";
                using var other = await client.PostAsJsonAsync(RetirementRoute, Sign(otherDecision));
                Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            }

            // A new host with changed valid configuration must still return the original frozen H.
            await using var restarted = RetirementFactory(clock, root.FullName, changedConfiguration: true);
            using var restartedClient = CreateClient(restarted);
            using var replay = await restartedClient.PostAsJsonAsync(RetirementRoute, Sign(Fields()));
            Assert.Equal(accepted, await replay.Content.ReadFromJsonAsync<RetireTemplateAssetResponse>());
            using var verification = restarted.Services.CreateScope();
            var db = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var collector = verification.ServiceProvider.GetRequiredService<PostgreSqlFileStorageGarbageCollector>();
            foreach (var advance in new[] { TimeSpan.FromDays(7), TimeSpan.FromDays(1), TimeSpan.FromDays(83) })
            {
                clock.Advance(advance);
                var result = await collector.CollectAsync(default);
                Assert.Equal(0, result.FormalFilesPhysicallyDeleted);
                Assert.Equal(0, result.LocalTusFilesRemoved);
                Assert.Equal("physical-hold", (await db.StoredFiles.AsNoTracking().SingleAsync()).Status);
                Assert.Null((await db.StoredFiles.AsNoTracking().SingleAsync()).PhysicalDeleteAfterUtc);
                Assert.Single(await db.UploadSessions.AsNoTracking().ToArrayAsync());
                var tombstone = await db.TemplateAssetRetirements.AsNoTracking().SingleAsync();
                Assert.Equal(2592000, tombstone.ReplayHorizonSeconds);
                Assert.Equal(604800, tombstone.PhysicalGraceSeconds);
                Assert.Equal("physical-hold", tombstone.Status);
                var accessor = verification.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>();
                Assert.True(accessor.TryGet(out var bytes));
                Assert.True(bytes.Exists("retirement-upload"));
                Assert.Equal(12, bytes.GetOffset("retirement-upload"));
            }
            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20260824091513_AddDurableUploadCommitProtocol"));
            Assert.Contains("Retirement receipts exist", downgrade.MessageText, StringComparison.Ordinal);
            Assert.Single(await db.TemplateAssetRetirements.AsNoTracking().ToArrayAsync());
        }
        finally { root.Delete(recursive: true); }
    }

    [FileStorageRealPostgresFact]
    public async Task Retirement_verifier_rejects_each_wire_and_resource_constraint_without_writes_on_postgres()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = RetirementFactory(new FakeTimeProvider(Epoch));
        using var client = CreateClient(factory);
        await SeedRetirementAssetAsync(factory, bytes: false);
        var cases = new List<(string Name, RetireTemplateAssetRequest Request)>();
        foreach (var (index, value) in new (int, string)[]
        {
            (0,"2"), (1,"SHA-256"), (2,"wrong-issuer"), (3,"wrong-audience"), (4,"00"),
            (5,"invalid"), (6,"invalid-decision"), (7,"2"), (8,"0"), (9,"0"), (10,"0"),
            (9,"-1"), (10,"-1"), (9,"7776000"), (11,"wrong-org"), (12,"wrong-env"),
            (13,"wrong-file"), (14,$"sha256:{new string('a',64)}"), (15,"other-service"),
            (16,"other-owner-type"), (17,"other-owner"), (18,"attachment")
        })
        {
            var fields = Fields(); fields[index] = value;
            cases.Add(($"field-{index}-{value}", Sign(fields)));
        }
        foreach (var (issued, expires) in new[] { (0,0), (0,-1), (0,301), (300,301), (301,302), (-301,-300), (-302,-301) })
        {
            var fields = Fields();
            fields[4] = (Epoch.ToUnixTimeSeconds()+issued).ToString(CultureInfo.InvariantCulture);
            fields[5] = (Epoch.ToUnixTimeSeconds()+expires).ToString(CultureInfo.InvariantCulture);
            cases.Add(($"clock-{issued}-{expires}", Sign(fields)));
        }
        var wire = Wire(Fields());
        cases.Add(("bom", SignBytes([0xef,0xbb,0xbf,..wire])));
        cases.Add(("crlf", SignBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(wire).Replace("\n", "\r\n")))));
        cases.Add(("invalid-utf8", SignBytes([..wire[..^1],0xff])));
        cases.Add(("missing-field", Sign(Fields()[..^1])));
        cases.Add(("duplicate-field", Sign([..Fields(),Fields()[18]])));
        var reordered = Fields(); (reordered[11], reordered[12]) = (reordered[12], reordered[11]);
        cases.Add(("field-order", Sign(reordered)));
        cases.Add(("character-length-instead-of-byte-length", SignBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(wire).Replace("9:模板甲", "3:模板甲")))));
        cases.Add(("leading-zero-length", SignBytes(Encoding.UTF8.GetBytes("0"+Encoding.UTF8.GetString(wire)))));
        cases.Add(("wrong-key", SignBytes(wire, Encoding.UTF8.GetBytes(new string('z',32)))));
        cases.Add(("signature-missing", Sign(Fields()) with { Signature = "" }));
        cases.Add(("signature-tampered", Sign(Fields()) with { Signature = new string('A',43) }));
        cases.Add(("signature-padding", Sign(Fields()) with { Signature = Sign(Fields()).Signature+"=" }));
        cases.Add(("payload-padding", Sign(Fields()) with { Payload = Sign(Fields()).Payload+"=" }));
        cases.Add(("payload-missing", Sign(Fields()) with { Payload = "" }));
        foreach (var item in cases)
        {
            using var response = await client.PostAsJsonAsync(RetirementRoute, item.Request);
            Assert.True((int)response.StatusCode is >= 400 and < 500, item.Name);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.TemplateAssetRetirements.ToArrayAsync());
            Assert.Equal("available", (await db.StoredFiles.SingleAsync()).Status);
            var usage = await scope.ServiceProvider.GetRequiredService<IFileStorageService>()
                .GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default);
            Assert.Equal(12, usage.Value!.UsedBytes);
        }
        client.DefaultRequestHeaders.Authorization = null;
        using var unauthenticated = await client.PostAsJsonAsync(RetirementRoute, Sign(Fields()));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [FileStorageRealPostgresFact]
    public async Task Retirement_concurrent_duplicate_waits_for_row_lock_and_rollback_is_atomic_on_postgres()
    {
        await ResetFileStorageSchemaAsync();
        await using var factory = RetirementFactory(new FakeTimeProvider(Epoch));
        await SeedRetirementAssetAsync(factory, bytes: false);
        var gate = new RetirementSaveGate();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(LaneConnectionString);
        await using var first = new ApplicationDbContext(options.Options);
        await using var held = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(LaneConnectionString).AddInterceptors(gate).Options);
        var request = new TemplateAssetRetirementProof(Options(), new FakeTimeProvider(Epoch)).Verify(Sign(Fields()))!;
        var pending = new TemplateAssetRetirementStore(held).AcceptAsync(request, Options().Storage, Epoch, default);
        await TestTimeout.RunAsync("retirement before save", async ct => await gate.Entered.Task.WaitAsync(ct), TimeSpan.FromSeconds(15));
        await first.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)first.Database.GetDbConnection()).ProcessID;
        var contender = new TemplateAssetRetirementStore(first).AcceptAsync(request, Options().Storage, Epoch.AddSeconds(1), default);
        try
        {
            await Eventually.AssertAsync("duplicate waits on production retirement row lock", async ct =>
            {
                await using var connection = new NpgsqlConnection(LaneConnectionString);
                await connection.OpenAsync(ct);
                await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')", connection);
                query.Parameters.AddWithValue("pid", pid);
                Assert.True((bool)(await query.ExecuteScalarAsync(ct))!);
            }, new(TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(20), []));
        }
        finally { gate.Release.TrySetResult(); }
        var results = await TestTimeout.RunAsync("retirement duplicate commit", async ct => await Task.WhenAll(pending, contender).WaitAsync(ct), TimeSpan.FromSeconds(15));
        Assert.All(results, result => Assert.NotNull(result.Receipt));
        Assert.Equal(results[0].Receipt!.AcceptedAtUtc, results[1].Receipt!.AcceptedAtUtc);
        Assert.Equal(1, await first.TemplateAssetRetirements.CountAsync());

        // A real database failure after modifying the tracked file rolls back both persisted facts.
        await ResetFileStorageSchemaAsync();
        await using var rollbackFactory = RetirementFactory(new FakeTimeProvider(Epoch));
        await SeedRetirementAssetAsync(rollbackFactory, bytes: false);
        await using var failed = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(LaneConnectionString).AddInterceptors(new RetirementFailure()).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TemplateAssetRetirementStore(failed).AcceptAsync(request, Options().Storage, Epoch, default));
        using var verify = rollbackFactory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.TemplateAssetRetirements.ToArrayAsync());
        Assert.Equal("available", (await db.StoredFiles.SingleAsync()).Status);
        Assert.Equal(12, (await verify.ServiceProvider.GetRequiredService<IFileStorageService>()
            .GetUsageAsync(new("retirement-org", "retirement-env", "barcode-label-template"), default)).Value!.UsedBytes);
    }

    private static WebApplicationFactory<Program> RetirementFactory(FakeTimeProvider clock, string? root = null, bool changedConfiguration = false) =>
        CreateFactory(LaneConnectionString, autoMigrate: true).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FileStorage:GarbageCollection:IntervalSeconds", "300");
            if (root is not null)
            {
                builder.UseSetting("FileStorage:Tus:RootPath", root);
                builder.UseSetting("FileStorage:UploadProvider", "tus");
            }
            if (changedConfiguration)
                builder.UseSetting("FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds", "3456000");
            builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); });
        });

    private static async Task SeedRetirementAssetAsync(WebApplicationFactory<Program> factory, bool bytes = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fields = Fields();
        var file = StoredFileRecord.Create(fields[13], fields[11], fields[12], fields[15], fields[16], fields[17], fields[18],
            "template.json", "application/vnd.nerv-iip.label-template+json", 12, fields[14], "retirement-object", "available", Epoch, Epoch);
        var session = UploadSessionRecord.Create("retirement-upload", file.FileId, file.OrganizationId, file.EnvironmentId,
            file.OwnerService, file.OwnerType, file.OwnerId, file.FilePurpose, file.FileName, file.ContentType,
            12, file.Checksum, file.ObjectKey, "tus", Epoch, Epoch.AddMinutes(15));
        session.BeginCommit("retirement-upload-commit", file.Checksum, Epoch);
        session.MarkCompleted(Epoch);
        db.StoredFiles.Add(file); db.UploadSessions.Add(session);
        await db.SaveChangesAsync();
        if (bytes)
        {
            Assert.True(scope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var store));
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("0123456789ab"));
            await store.AppendAsync(session.UploadSessionId, 0, content, default);
        }
    }

    private sealed class RetirementSaveGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await TestTimeout.RunAsync("release retirement save", async ct => await Release.Task.WaitAsync(ct), TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private sealed class RetirementFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after SQL writes and before transaction commit.");
    }
}
