using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class ProductionReportSerialNumberPostgresTests
{
    private const string PreviousMigration = "20260910084403_WidenMesDefectDispositionReferenceIdForQualityProducerWidth";
    private const string TargetMigrationSuffix = "_AddMesProductionReportSerialNumbers";

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

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static async Task<ProductionReport> SeedReportAsync(
        ApplicationDbContext db,
        string organizationId,
        string environmentId,
        string reportNo,
        string? serialNo)
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
        var report = CreateReport(organizationId, environmentId, reportNo, serialNo);
        db.ProductionReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }

    private static ProductionReport CreateReport(
        string organizationId,
        string environmentId,
        string reportNo,
        string? serialNo) =>
        ProductionReport.Record(
            organizationId,
            environmentId,
            reportNo,
            $"WO-{reportNo}",
            $"OP-{reportNo}",
            1m,
            0m,
            false,
            DateTimeOffset.Parse("2026-08-30T09:20:00Z"),
            serialNo: serialNo);

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
