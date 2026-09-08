using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class DowntimeReasonCodeMigrationPostgresTests
{
    private const string PreviousMigration = "20260829103216_AddMesReworkWorkOrderFoundation";
    private const string TargetMigrationSuffix = "_MigrateDowntimeReasonTextsToCodes";

    // Contract: ReferenceData + ProviderBehavior + Regression. Authority: owner-approved #2681 C
    // mapping and the Maintenance downtime-reason producer. Wrong CASE arms, scope filtering,
    // collateral updates, or a non-idempotent second Up must fail on PostgreSQL.
    [MesRealPostgresFact]
    public async Task Legacy_reasons_migrate_once_across_all_scopes_and_repeat_stably_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using var context = new ApplicationDbContext(options, new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(context);
        var migrations = context.Database.GetMigrations();
        var targetMigration = Assert.Single(migrations, migration =>
            migration.EndsWith(TargetMigrationSuffix, StringComparison.Ordinal));
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await InsertLegacyAndControlRowsAsync(connection);

        await migrator.MigrateAsync(targetMigration);
        var firstUp = await ReadRowsAsync(connection);
        AssertExpectedRows(firstUp);

        // Down intentionally preserves migrated business facts. Removing the migration history and
        // applying Up again therefore exercises the same idempotent SQL against already-coded rows.
        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(firstUp, await ReadRowsAsync(connection));
        await migrator.MigrateAsync(targetMigration);
        Assert.Equal(firstUp, await ReadRowsAsync(connection));
    }

    private static async Task InsertLegacyAndControlRowsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mes.work_center_unavailabilities
                (id, organization_id, environment_id, downtime_event_no, work_center_id,
                 from_utc, to_utc, reason, device_asset_id)
            VALUES
                ('019ca000-0000-7000-8000-000000000001', NULL, NULL,
                 'LEGACY-GLOBAL-SETUP', 'WC-01', '2026-08-01T00:00:00Z', NULL, '换型调整', 'DEV-01'),
                ('019ca000-0000-7000-8000-000000000002', 'org-a', 'env-a',
                 'LEGACY-A-MECH', 'WC-02', '2026-08-01T01:00:00Z', NULL, '设备故障', 'DEV-02'),
                ('019ca000-0000-7000-8000-000000000003', 'org-a', 'env-a',
                 'LEGACY-A-MATERIAL', 'WC-03', '2026-08-01T02:00:00Z', NULL, '缺料待工', 'DEV-03'),
                ('019ca000-0000-7000-8000-000000000004', 'org-b', 'env-b',
                 'LEGACY-B-PM', 'WC-04', '2026-08-01T03:00:00Z', NULL, '计划保养', 'DEV-04'),
                ('019ca000-0000-7000-8000-000000000005', 'org-b', 'env-b',
                 'LEGACY-B-QUALITY', 'WC-05', '2026-08-01T04:00:00Z', NULL, '质量停机', 'DEV-05'),
                ('019ca000-0000-7000-8000-000000000006', 'org-a', 'env-a',
                 'CONTROL-CODED', 'WC-06', '2026-08-01T05:00:00Z', NULL, 'DT-ELEC', 'DEV-06'),
                ('019ca000-0000-7000-8000-000000000007', 'org-b', 'env-b',
                 'CONTROL-UNKNOWN', 'WC-07', '2026-08-01T06:00:00Z', NULL, '供应商停机', 'DEV-07');
            """;
        Assert.Equal(7, await command.ExecuteNonQueryAsync());
    }

    private static async Task<Dictionary<string, DowntimeRow>> ReadRowsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT downtime_event_no, organization_id, environment_id, reason
            FROM mes.work_center_unavailabilities
            ORDER BY downtime_event_no
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, DowntimeRow>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            rows.Add(
                reader.GetString(0),
                new DowntimeRow(
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3)));
        }

        return rows;
    }

    private static void AssertExpectedRows(IReadOnlyDictionary<string, DowntimeRow> rows)
    {
        Assert.Equal(7, rows.Count);
        Assert.Equal(new DowntimeRow(null, null, "DT-SETUP"), rows["LEGACY-GLOBAL-SETUP"]);
        Assert.Equal(new DowntimeRow("org-a", "env-a", "DT-MECH"), rows["LEGACY-A-MECH"]);
        Assert.Equal(new DowntimeRow("org-a", "env-a", "DT-MATERIAL"), rows["LEGACY-A-MATERIAL"]);
        Assert.Equal(new DowntimeRow("org-b", "env-b", "DT-PM"), rows["LEGACY-B-PM"]);
        Assert.Equal(new DowntimeRow("org-b", "env-b", "DT-QUALITY"), rows["LEGACY-B-QUALITY"]);
        Assert.Equal(new DowntimeRow("org-a", "env-a", "DT-ELEC"), rows["CONTROL-CODED"]);
        Assert.Equal(new DowntimeRow("org-b", "env-b", "供应商停机"), rows["CONTROL-UNKNOWN"]);
    }

    private sealed record DowntimeRow(string? OrganizationId, string? EnvironmentId, string Reason);
}
