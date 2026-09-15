using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Coding;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class ProductionReportSerialNumberPostgresTests
{
    private const string PreviousMigration = "20260910084403_WidenMesDefectDispositionReferenceIdForQualityProducerWidth";
    private const string TargetMigrationSuffix = "_AddMesProductionReportSerialNumbers";
    private const string IntentMigrationSuffix = "_AddMesProductionReportIntentFingerprint";

    [MesRealPostgresFact]
    public async Task Intent_fingerprint_migration_is_nullable_varchar_256_and_survives_down_up()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var db = CreateDbContext(options);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrator = db.GetService<IMigrator>();
        var previousMigration = Assert.Single(
            db.Database.GetMigrations(),
            migration => migration.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
        var targetMigration = Assert.Single(
            db.Database.GetMigrations(),
            migration => migration.EndsWith(IntentMigrationSuffix, StringComparison.Ordinal));

        await migrator.MigrateAsync(previousMigration);
        Assert.False(await ColumnExistsAsync(db, "report_intent_fingerprint"));

        await migrator.MigrateAsync(targetMigration);
        await AssertIntentFingerprintColumnAsync(db);

        await migrator.MigrateAsync(previousMigration);
        Assert.False(await ColumnExistsAsync(db, "report_intent_fingerprint"));

        await migrator.MigrateAsync(targetMigration);
        await AssertIntentFingerprintColumnAsync(db);
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_intent_receipt_round_trips_exact_value_and_keeps_nullable_crossings_conflicting()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateIntentFactory(new IntentReceiptSaveGate());
        await StartMigrateAndSeedIntentScopeAsync(factory, "WO-INTENT", "OP-INTENT");

        const string fingerprint = "  opaque:v1:sha256:ABC==  ";
        var command = IntentCommand("intent-roundtrip-001", fingerprint, "WO-INTENT", "OP-INTENT");
        ProductionReportCommandResult first;
        await using (var commandScope = factory.Services.CreateAsyncScope())
        {
            first = await commandScope.ServiceProvider.GetRequiredService<ISender>()
                .Send(command, CancellationToken.None);
        }

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // 同 scope/key 的其它编码规则是合法 decoy；恢复读面必须固定 production-report ruleKey。
        db.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
            "org-001",
            "env-dev",
            "work-order",
            command.IdempotencyKey,
            "WO-DECOY",
            "decoy-fingerprint",
            DateTimeOffset.Parse("2026-09-14T08:01:00Z")));
        await db.SaveChangesAsync();
        await ReinsertIntentSerialsOutOfSequenceAsync(db, first.ReportNo);
        var receipt = await new GetProductionReportByIdempotencyKeyQueryHandler(db).Handle(
            new GetProductionReportByIdempotencyKeyQuery("org-001", "env-dev", "  intent-roundtrip-001  "),
            CancellationToken.None);
        Assert.Equal(fingerprint, receipt.ReportIntentFingerprint);
        Assert.Equal(first.Id, receipt.ProductionReportId);
        Assert.Equal(first.ReportNo, receipt.ReportNo);
        Assert.Equal(["SN-B", "SN-A"], receipt.SerialNumbers);

        foreach (var request in new[]
                 {
                     new GetProductionReportByIdempotencyKeyQuery("org-other", "env-dev", command.IdempotencyKey),
                     new GetProductionReportByIdempotencyKeyQuery("org-001", "env-other", command.IdempotencyKey),
                     new GetProductionReportByIdempotencyKeyQuery("org-001", "env-dev", "intent-missing"),
                 })
        {
            var hidden = await Assert.ThrowsAsync<KnownException>(() =>
                new GetProductionReportByIdempotencyKeyQueryHandler(db).Handle(request, CancellationToken.None));
            Assert.Equal("未找到生产报工。", hidden.Message);
        }

        var replay = await assertionScope.ServiceProvider.GetRequiredService<ISender>()
            .Send(command, CancellationToken.None);
        Assert.Equal(first.Id, replay.Id);

        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() =>
            assertionScope.ServiceProvider.GetRequiredService<ISender>().Send(
                command with { ReportIntentFingerprint = "opaque:different" },
                CancellationToken.None));
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() =>
            assertionScope.ServiceProvider.GetRequiredService<ISender>().Send(
                command with { ReportIntentFingerprint = null },
                CancellationToken.None));

        var nullCommand = IntentCommand("intent-null-001", null, "WO-INTENT", "OP-INTENT") with
        {
            GoodQuantity = 2m,
            SerialNumbers = ["SN-C", "SN-D"],
        };
        _ = await assertionScope.ServiceProvider.GetRequiredService<ISender>()
            .Send(nullCommand, CancellationToken.None);
        var nullReceipt = await new GetProductionReportByIdempotencyKeyQueryHandler(db).Handle(
            new GetProductionReportByIdempotencyKeyQuery("org-001", "env-dev", nullCommand.IdempotencyKey),
            CancellationToken.None);
        Assert.Null(nullReceipt.ReportIntentFingerprint);
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() =>
            assertionScope.ServiceProvider.GetRequiredService<ISender>().Send(
                nullCommand with { ReportIntentFingerprint = "opaque:late" },
                CancellationToken.None));
        var nullReceiptAfterConflict = await new GetProductionReportByIdempotencyKeyQueryHandler(db).Handle(
            new GetProductionReportByIdempotencyKeyQuery("org-001", "env-dev", nullCommand.IdempotencyKey),
            CancellationToken.None);
        Assert.Null(nullReceiptAfterConflict.ReportIntentFingerprint);
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_concurrent_same_and_different_fingerprints_commit_one_atomic_intent_receipt()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await VerifyConcurrentIntentAsync(sameFingerprint: true);
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await VerifyConcurrentIntentAsync(sameFingerprint: false);
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_serial_failure_rolls_back_report_idempotency_receipt_and_serials_together()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateIntentFactory(new IntentReceiptSaveGate());
        await StartMigrateAndSeedIntentScopeAsync(factory, "WO-ATOMIC", "OP-ATOMIC");
        await InstallSerialFailureTriggerAsync();

        await using (var commandScope = factory.Services.CreateAsyncScope())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => commandScope.ServiceProvider.GetRequiredService<ISender>().Send(
                IntentCommand("intent-atomic-001", "opaque:atomic", "WO-ATOMIC", "OP-ATOMIC"),
                CancellationToken.None));
        }

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.ProductionReports.CountAsync());
        Assert.Equal(0, await db.ProductionReportSerialNumbers.CountAsync());
        Assert.Equal(0, await db.CodeIdempotencyKeys.CountAsync(x =>
            x.RuleKey == "production-report" && x.IdempotencyKey == "intent-atomic-001"));
        var missing = await Assert.ThrowsAsync<KnownException>(() =>
            new GetProductionReportByIdempotencyKeyQueryHandler(db).Handle(
                new GetProductionReportByIdempotencyKeyQuery("org-001", "env-dev", "intent-atomic-001"),
                CancellationToken.None));
        Assert.Equal("未找到生产报工。", missing.Message);
    }

    [MesRealPostgresFact]
    public async Task Migration_backfills_only_forward_nonblank_legacy_serials_and_survives_down_up()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var db = CreateDbContext(options);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrator = db.GetService<IMigrator>();
        var targetMigration = Assert.Single(
            db.Database.GetMigrations(),
            migration => migration.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
        await migrator.MigrateAsync(PreviousMigration);
        await AddCurrentModelCompatibilityColumnAsync(db);

        var original = await SeedReportAsync(db, "org-001", "env-dev", "PR-LEGACY", "SN-LEGACY");
        _ = await SeedReportAsync(db, "org-001", "env-dev", "PR-BLANK", null);
        db.ProductionReports.Add(ProductionReport.Reverse(
            original,
            "PR-LEGACY-REV",
            DateTimeOffset.Parse("2026-08-30T09:30:00Z"),
            "legacy reversal",
            "operator-1"));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE mes.production_reports SET serial_no = U&'\00A0SN-LEGACY\3000' WHERE report_no = 'PR-LEGACY';
            UPDATE mes.production_reports SET serial_no = ' SN-LEGACY ' WHERE report_no = 'PR-LEGACY-REV';
            UPDATE mes.production_reports SET serial_no = U&'\00A0\3000' WHERE report_no = 'PR-BLANK';
            """);

        await migrator.MigrateAsync(targetMigration);
        await AssertLegacyBackfillAsync(db);

        await migrator.MigrateAsync(PreviousMigration);
        Assert.False(await TableExistsAsync(db));
        Assert.Equal(3, await db.ProductionReports.CountAsync());
        Assert.Equal("\u00A0SN-LEGACY\u3000", await db.ProductionReports
            .Where(x => x.ReportNo == "PR-LEGACY")
            .Select(x => x.SerialNo)
            .SingleAsync());

        await migrator.MigrateAsync(targetMigration);
        await AssertLegacyBackfillAsync(db);
    }

    [MesRealPostgresFact]
    public async Task Migration_fails_closed_when_forward_legacy_reports_share_a_trimmed_scoped_serial()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var db = CreateDbContext(options);
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrator = db.GetService<IMigrator>();
        var targetMigration = Assert.Single(
            db.Database.GetMigrations(),
            migration => migration.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
        await migrator.MigrateAsync(PreviousMigration);
        await AddCurrentModelCompatibilityColumnAsync(db);

        _ = await SeedReportAsync(db, "org-001", "env-dev", "PR-DUP-A", "SN-DUP");
        _ = await SeedReportAsync(db, "org-001", "env-dev", "PR-DUP-B", "SN-DUP");
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE mes.production_reports SET serial_no = U&'\\3000SN-DUP\\00A0' WHERE report_no = 'PR-DUP-B'");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(targetMigration));

        Assert.Equal("23000", failure.SqlState);
        Assert.Contains("AddMesProductionReportSerialNumbers aborted", failure.MessageText, StringComparison.Ordinal);
        Assert.Contains("org-001 / env-dev / SN-DUP / PR-DUP-A, PR-DUP-B", failure.MessageText, StringComparison.Ordinal);
        Assert.False(await TableExistsAsync(db));
        Assert.Equal(2, await db.ProductionReports.CountAsync());
        Assert.DoesNotContain(
            await db.Database.GetAppliedMigrationsAsync(),
            migration => migration.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
    }

    [MesRealPostgresFact]
    public Task Legacy_v1_receipt_without_serial_replays_the_existing_report_through_the_new_handler_on_postgres() =>
        VerifyLegacyV1ReceiptReplayAsync(null, 1, ProductionSerialTrackingPolicies.None);

    [MesRealPostgresFact]
    public Task Legacy_v1_receipt_with_single_serial_replays_the_existing_report_through_the_new_handler_on_postgres() =>
        VerifyLegacyV1ReceiptReplayAsync("  SN-LEGACY  ", 4, " none ");

    private static async Task VerifyLegacyV1ReceiptReplayAsync(
        string? serialNo,
        int goodQuantity,
        string serialTrackingPolicy)
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        var reportNo = serialNo is null ? "PR-UPGRADE-NONE" : "PR-UPGRADE-SERIAL";
        var idempotencyKey = serialNo is null ? "report-upgrade-none" : "report-upgrade-serial";
        var producedLotNo = serialNo is null ? "LOT-UPGRADE-NONE" : "LOT-UPGRADE-SERIAL";
        var reportedAtUtc = DateTimeOffset.Parse("2026-08-30T09:20:00Z");
        var request = new RecordProductionReportCommand(
            "org-001",
            "env-dev",
            $"WO-{reportNo}",
            $"OP-{reportNo}",
            goodQuantity,
            0m,
            false,
            reportedAtUtc,
            idempotencyKey,
            ProducedLotNo: producedLotNo,
            SerialNo: serialNo,
            SerialTrackingPolicy: serialTrackingPolicy);

        await using (var seed = CreateDbContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(seed);
            await seed.Database.MigrateAsync();
            var report = await SeedReportAsync(
                seed,
                "org-001",
                "env-dev",
                reportNo,
                serialNo,
                goodQuantity,
                producedLotNo);
            if (serialNo is not null)
            {
                seed.ProductionReportSerialNumbers.Add(
                    ProductionReportSerialNumber.CreateForReport(report, [serialNo])[0]);
            }

            seed.CodeIdempotencyKeys.Add(new CodeIdempotencyKey(
                request.OrganizationId,
                request.EnvironmentId,
                "production-report",
                request.IdempotencyKey,
                report.ReportNo,
                LegacyV1Fingerprint(request),
                reportedAtUtc));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IMediator>(new NoopMediator());
        services.AddMesPostgreSqlPersistence(MesPostgresLaneDatabase.ConnectionString);
        services.AddScoped<MesCodingService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new RecordProductionReportCommandHandler(
            db,
            TestProductionReportOeeDimensionSnapshotProvider.Instance,
            TestMesFirstArticleGate.Allowing,
            scope.ServiceProvider.GetRequiredService<MesCodingService>());

        var replay = await handler.Handle(request, CancellationToken.None);

        Assert.Equal(reportNo, replay.ReportNo);
        Assert.Equal(serialNo is null ? [] : [serialNo.Trim()], replay.SerialNumbers);
        Assert.Equal(1, await db.ProductionReports.CountAsync());
        Assert.Equal(1, await db.CodeIdempotencyKeys.CountAsync());
        Assert.Equal(serialNo is null ? 0 : 1, await db.ProductionReportSerialNumbers.CountAsync());

        var changedSerial = request with { SerialNo = "SN-CHANGED" };
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() =>
            handler.Handle(changedSerial, CancellationToken.None));
        Assert.Equal(1, await db.ProductionReports.CountAsync());
        Assert.Equal(1, await db.CodeIdempotencyKeys.CountAsync());
    }

    [MesRealPostgresFact]
    public async Task PostgreSQL_persists_ordered_serials_and_enforces_ordinal_scoped_uniqueness_and_report_fk()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var setup = CreateDbContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            _ = await SeedReportAsync(setup, "org-001", "env-dev", "PR-A", null);
            _ = await SeedReportAsync(setup, "org-001", "env-dev", "PR-B", null);
            _ = await SeedReportAsync(setup, "org-001", "env-other", "PR-C", null);
            _ = await SeedReportAsync(setup, "org-other", "env-dev", "PR-D", null);
            _ = await SeedReportAsync(setup, "org-001", "env-dev", "PR-DUP", null);
            _ = await SeedReportAsync(setup, "org-001", "env-dev", "PR-CONSTRAINT", null);
            setup.ProductionReportSerialNumbers.AddRange(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-001", "env-dev", "PR-A", null), ["SN-B", "SN-A"]));
            setup.ProductionReportSerialNumbers.Add(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-001", "env-dev", "PR-B", null), ["sn-b"])[0]);
            setup.ProductionReportSerialNumbers.Add(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-001", "env-other", "PR-C", null), ["SN-B"])[0]);
            setup.ProductionReportSerialNumbers.Add(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-other", "env-dev", "PR-D", null), ["SN-B"])[0]);
            await setup.SaveChangesAsync();
        }

        await using (var nonPositiveSequence = CreateDbContext(options))
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                nonPositiveSequence.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO mes.production_report_serial_numbers
                        (id, organization_id, environment_id, report_no, sequence_no, serial_number)
                    VALUES ({Guid.CreateVersion7()}, {"org-001"}, {"env-dev"}, {"PR-CONSTRAINT"}, {0}, {"SN-ZERO"});
                    """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal("ck_production_report_serial_numbers_sequence_positive", failure.ConstraintName);
        }

        await using (var duplicateSequence = CreateDbContext(options))
        {
            duplicateSequence.ProductionReportSerialNumbers.AddRange(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-001", "env-dev", "PR-CONSTRAINT", null), ["SN-ONE"]));
            await duplicateSequence.SaveChangesAsync();

            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                duplicateSequence.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO mes.production_report_serial_numbers
                        (id, organization_id, environment_id, report_no, sequence_no, serial_number)
                    VALUES ({Guid.CreateVersion7()}, {"org-001"}, {"env-dev"}, {"PR-CONSTRAINT"}, {1}, {"SN-TWO"});
                    """));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
            Assert.Equal("ux_production_report_serial_numbers_scope_report_sequence", failure.ConstraintName);
        }

        await using (var read = CreateDbContext(options))
        {
            var reportASerials = await read.ProductionReportSerialNumbers
                .Where(x => x.ReportNo == "PR-A")
                .OrderBy(x => x.SequenceNo)
                .Select(x => new { x.SequenceNo, x.SerialNumber })
                .ToArrayAsync();
            Assert.Equal([(1, "SN-B"), (2, "SN-A")], reportASerials.Select(x => (x.SequenceNo, x.SerialNumber)));

            var detail = await new GetProductionReportQueryHandler(read).Handle(
                new GetProductionReportQuery("org-001", "env-dev", "PR-A"),
                CancellationToken.None);
            Assert.Equal(["SN-B", "SN-A"], detail.Report.SerialNumbers);

            var trace = await new GetBatchTraceabilityQueryHandler(read).Handle(
                new GetBatchTraceabilityQuery("org-001", "env-dev", "SN-B"),
                CancellationToken.None);
            Assert.Contains(trace.Nodes, x => x.NodeId == "PR-A" && x.NodeType == MesTraceabilityNodeType.ProductionReport);
            Assert.Contains(trace.Edges, x => x.FromNodeId == "PR-A" && x.ToNodeId == "SN-B" && x.RelationType == "produced-serial");
            Assert.DoesNotContain(trace.Nodes, x => x.NodeId is "PR-C" or "PR-D");
        }

        await using (var duplicate = CreateDbContext(options))
        {
            duplicate.ProductionReportSerialNumbers.Add(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-001", "env-dev", "PR-DUP", null), ["SN-B"])[0]);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("ux_production_report_serial_numbers_scope_serial", postgres.ConstraintName);
        }

        await using (var orphan = CreateDbContext(options))
        {
            orphan.ProductionReportSerialNumbers.Add(
                ProductionReportSerialNumber.CreateForReport(
                    CreateReport("org-001", "env-dev", "PR-MISSING", null), ["SN-ORPHAN"])[0]);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => orphan.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
            Assert.Equal("fk_production_report_serial_numbers_reports", postgres.ConstraintName);
        }
    }

    private static async Task VerifyConcurrentIntentAsync(bool sameFingerprint)
    {
        var gate = new IntentReceiptSaveGate();
        await using var factory = CreateIntentFactory(gate);
        var suffix = sameFingerprint ? "SAME" : "DIFF";
        await StartMigrateAndSeedIntentScopeAsync(factory, $"WO-{suffix}", $"OP-{suffix}");
        var first = IntentCommand($"intent-concurrent-{suffix}", "opaque:first", $"WO-{suffix}", $"OP-{suffix}");
        var second = sameFingerprint ? first : first with { ReportIntentFingerprint = "opaque:second" };

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        gate.Enable();
        IntentOutcome[] outcomes;
        try
        {
            outcomes = await Task.WhenAll(
                CaptureIntentAsync(firstScope.ServiceProvider.GetRequiredService<ISender>(), first),
                CaptureIntentAsync(secondScope.ServiceProvider.GetRequiredService<ISender>(), second));
        }
        finally
        {
            gate.Release();
        }

        if (sameFingerprint)
        {
            Assert.All(outcomes, outcome => Assert.Null(outcome.Exception));
            Assert.Equal(outcomes[0].Result!.Id, outcomes[1].Result!.Id);
        }
        else
        {
            Assert.Single(outcomes, outcome => outcome.Result is not null);
            Assert.IsType<MesIdempotencyConflictException>(
                Assert.Single(outcomes, outcome => outcome.Exception is not null).Exception);
        }

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ProductionReports.CountAsync());
        Assert.Equal(1, await db.CodeIdempotencyKeys.CountAsync(x =>
            x.RuleKey == "production-report" && x.IdempotencyKey == first.IdempotencyKey));
        Assert.Equal(2, await db.ProductionReportSerialNumbers.CountAsync());
    }

    private static async Task<IntentOutcome> CaptureIntentAsync(ISender sender, RecordProductionReportCommand command)
    {
        try
        {
            return new(await sender.Send(command, CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            return new(null, exception);
        }
    }

    private static RecordProductionReportCommand IntentCommand(
        string idempotencyKey,
        string? fingerprint,
        string workOrderId,
        string operationTaskId) =>
        new(
            "org-001",
            "env-dev",
            workOrderId,
            operationTaskId,
            2m,
            0m,
            false,
            DateTimeOffset.Parse("2026-09-14T08:00:00Z"),
            idempotencyKey,
            SerialTrackingPolicy: ProductionSerialTrackingPolicies.OnProduction,
            SerialNumbers: ["SN-B", "SN-A"],
            ReportIntentFingerprint: fingerprint);

    private static WebApplicationFactory<Program> CreateIntentFactory(IntentReceiptSaveGate gate) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
                ["Messaging:Provider"] = "InMemory",
                ["Cap:Version"] = "i3433-intent",
                ["InternalService:BearerToken"] = "test-internal-token",
            };
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IMesFirstArticleGate>(_ => TestMesFirstArticleGate.Allowing);
                services.AddScoped<IProductionReportOeeDimensionSnapshotProvider>(
                    _ => TestProductionReportOeeDimensionSnapshotProvider.Instance);
                services.AddSingleton(gate);
                services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
                    options.AddInterceptors(serviceProvider.GetRequiredService<IntentReceiptSaveGate>()));
            });
        });

    private static async Task StartMigrateAndSeedIntentScopeAsync(
        WebApplicationFactory<Program> factory,
        string workOrderId,
        string operationTaskId)
    {
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var due = DateTimeOffset.Parse("2026-09-15T08:00:00Z");
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", workOrderId, "SKU-001", "PV-001", 20m, 10, due);
        workOrder.MarkReleased();
        workOrder.ClearDomainEvents();
        var task = OperationTask.Queue(
            "org-001", "env-dev", workOrderId, operationTaskId, 10, "WC-001", [],
            due.AddHours(-2), TimeSpan.FromHours(1), "SKU-001");
        task.Assign("operator-001", null, null, due.AddHours(-2));
        task.Start(due.AddHours(-1));
        db.WorkOrders.Add(workOrder);
        db.OperationTasks.Add(task);
        await db.SaveChangesAsync();
    }

    private static async Task InstallSerialFailureTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE FUNCTION mes.reject_intent_test_serial()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'injected production-report serial failure';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_intent_test_serial
            BEFORE INSERT ON mes.production_report_serial_numbers
            FOR EACH ROW EXECUTE FUNCTION mes.reject_intent_test_serial();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ReinsertIntentSerialsOutOfSequenceAsync(
        ApplicationDbContext db,
        string reportNo)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM mes.production_report_serial_numbers
            WHERE organization_id = {"org-001"}
              AND environment_id = {"env-dev"}
              AND report_no = {reportNo}
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO mes.production_report_serial_numbers
                (id, organization_id, environment_id, report_no, sequence_no, serial_number)
            VALUES ({Guid.Parse("019c9ce7-cd01-7b70-bbf0-8fc56d360002")}, {"org-001"}, {"env-dev"}, {reportNo}, {2}, {"SN-A"})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO mes.production_report_serial_numbers
                (id, organization_id, environment_id, report_no, sequence_no, serial_number)
            VALUES ({Guid.Parse("019c9ce7-cd01-7b70-bbf0-8fc56d360001")}, {"org-001"}, {"env-dev"}, {reportNo}, {1}, {"SN-B"})
            """);
        // 固定为 heap scan 后物理顺序是 2、1；只有生产查询自己的 ORDER BY 才能返回 1、2。
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            SET enable_indexscan = off;
            SET enable_indexonlyscan = off;
            SET enable_bitmapscan = off;
            """);
    }

    private static async Task AssertIntentFingerprintColumnAsync(ApplicationDbContext db)
    {
        var column = await db.Database.SqlQueryRaw<IntentColumnFact>("""
            SELECT data_type AS "DataType", character_maximum_length AS "MaximumLength", is_nullable AS "IsNullable"
            FROM information_schema.columns
            WHERE table_schema = 'mes'
              AND table_name = 'production_reports'
              AND column_name = 'report_intent_fingerprint'
            """).SingleAsync();
        Assert.Equal("character varying", column.DataType);
        Assert.Equal(ProductionReport.ReportIntentFingerprintMaxLength, column.MaximumLength);
        Assert.Equal("YES", column.IsNullable);
    }

    private static Task AddCurrentModelCompatibilityColumnAsync(ApplicationDbContext db) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE mes.production_reports
            ADD COLUMN report_intent_fingerprint character varying(256) NULL
            """);

    private static async Task<bool> ColumnExistsAsync(ApplicationDbContext db, string columnName) =>
        await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'mes'
                  AND table_name = 'production_reports'
                  AND column_name = {columnName}) AS "Value"
            """).SingleAsync();

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private sealed record IntentOutcome(ProductionReportCommandResult? Result, Exception? Exception);

    private sealed record IntentColumnFact(string DataType, int MaximumLength, string IsNullable);

    private sealed class IntentReceiptSaveGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> bothSavesArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivalCount;
        private int enabled;

        public void Enable() => Volatile.Write(ref enabled, 1);

        public void Release() => bothSavesArrived.TrySetResult(true);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref enabled) == 0 ||
                eventData.Context is null ||
                !eventData.Context.ChangeTracker.Entries<CodeIdempotencyKey>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.RuleKey == "production-report"))
            {
                return result;
            }

            var arrival = Interlocked.Increment(ref arrivalCount);
            if (arrival <= 2)
            {
                if (arrival == 2)
                {
                    bothSavesArrived.TrySetResult(true);
                }

                await bothSavesArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }
    }

    private static async Task<ProductionReport> SeedReportAsync(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string reportNo,
        string? serialNo,
        decimal goodQuantity = 1m,
        string? producedLotNo = null)
    {
        var now = DateTimeOffset.Parse("2026-08-30T09:00:00Z");
        db.WorkOrders.Add(WorkOrder.Create(
            organizationId,
            environmentId,
            $"WO-{reportNo}",
            "SKU-001",
            "PV-001",
            10m,
            10,
            now.AddHours(8)));
        db.OperationTasks.Add(OperationTask.Create(
            organizationId,
            environmentId,
            $"WO-{reportNo}",
            $"OP-{reportNo}",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-001",
            [],
            now,
            TimeSpan.FromMinutes(30),
            now,
            null,
            "SKU-001"));
        var report = CreateReport(organizationId, environmentId, reportNo, serialNo, goodQuantity, producedLotNo);
        db.ProductionReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }

    private static ProductionReport CreateReport(
        string organizationId,
        string environmentId,
        string reportNo,
        string? serialNo,
        decimal goodQuantity = 1m,
        string? producedLotNo = null) =>
        ProductionReport.Record(
            organizationId,
            environmentId,
            reportNo,
            $"WO-{reportNo}",
            $"OP-{reportNo}",
            goodQuantity,
            0m,
            false,
            DateTimeOffset.Parse("2026-08-30T09:20:00Z"),
            producedLotNo: producedLotNo,
            serialNo: serialNo);

    private static string LegacyV1Fingerprint(RecordProductionReportCommand request) =>
        MesCodingService.Fingerprint(
            request.WorkOrderId,
            request.OperationTaskId,
            request.GoodQuantity,
            request.ScrapQuantity,
            request.ReworkQuantity,
            request.CompletesOperation,
            request.ReportedAtUtc,
            request.ScrapReasonCode,
            request.DefectRecordNo,
            request.ProducedLotNo,
            request.SerialNo,
            request.Source,
            string.Empty);

    private static async Task AssertLegacyBackfillAsync(ApplicationDbContext db)
    {
        var serial = Assert.Single(await db.ProductionReportSerialNumbers.AsNoTracking().ToArrayAsync());
        Assert.Equal("org-001", serial.OrganizationId);
        Assert.Equal("env-dev", serial.EnvironmentId);
        Assert.Equal("PR-LEGACY", serial.ReportNo);
        Assert.Equal(1, serial.SequenceNo);
        Assert.Equal("SN-LEGACY", serial.SerialNumber);
    }

    private static async Task<bool> TableExistsAsync(ApplicationDbContext db) =>
        await db.Database.SqlQueryRaw<bool>(
            "SELECT to_regclass('mes.production_report_serial_numbers') IS NOT NULL AS \"Value\"")
            .SingleAsync();

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
