using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Diagnostics;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Queries;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryDirectoryPostgresTests
{
    [Fact]
    public async Task PostgreSql_fixture_allocates_parallel_run_scoped_ports_and_cleans_each_run()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            return;
        }

        var scopes = await Task.WhenAll(
            DirectoryPostgresScope.CreateAsync(),
            DirectoryPostgresScope.CreateAsync());
        try
        {
            var ports = scopes
                .Select(scope => new NpgsqlConnectionStringBuilder(scope.ConnectionString).Port)
                .ToArray();
            Assert.Equal(2, ports.Distinct().Count());
        }
        finally
        {
            await scopes[0].DisposeAsync();
            await scopes[1].DisposeAsync();
        }
    }

    [Fact]
    public async Task PostgreSql_executes_scoped_directories_and_uses_tenant_site_sku_index()
    {
        await using var postgres = await DirectoryPostgresScope.CreateAsync();
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddInventoryPostgreSqlPersistence(postgres.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DropInventorySchemaAsync(db);

        try
        {
            await db.Database.MigrateAsync();
            db.StockLocations.Add(StockLocation.CreateOrUpdate(
                null,
                "org-directory-pg",
                "env-directory-pg",
                "LOC-A-01",
                "bin",
                "SITE-A",
                null,
                "active"));
            db.StockLocations.Add(StockLocation.CreateOrUpdate(
                null,
                "org-directory-pg",
                "env-directory-pg",
                "LOC-B-01",
                "bin",
                "SITE-A",
                null,
                "active"));
            AddLedger(db, "LOC-A-01", "LOT-001", "SN-001");
            AddLedger(db, "LOC-A-02", "LOT-001", "SN-002");
            AddLedger(db, "LOC-B-01", "LOT-OTHER", "SN-OTHER", skuCode: "SKU-02");
            for (var index = 0; index < 1_500; index++)
            {
                AddLedger(
                    db,
                    $"NOISE-{index:D5}",
                    $"LOT-NOISE-{index:D5}",
                    $"SN-NOISE-{index:D5}",
                    organizationId: "org-directory-noise",
                    siteCode: "SITE-NOISE",
                    skuCode: "SKU-NOISE");
            }
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ANALYZE inventory.stock_ledgers");

            var handler = new ListInventoryDirectoryQueryHandler(db);
            var locations = await handler.Handle(
                new ListInventoryDirectoryQuery(
                    "org-directory-pg",
                    "env-directory-pg",
                    InventoryDirectoryTypes.Location,
                    SiteCode: "SITE-A",
                    SkuCode: "SKU-01",
                    Keyword: "loc-a"),
                CancellationToken.None);
            var batches = await handler.Handle(
                new ListInventoryDirectoryQuery(
                    "org-directory-pg",
                    "env-directory-pg",
                    InventoryDirectoryTypes.Batch,
                    SiteCode: "SITE-A",
                    SkuCode: "SKU-01",
                    Keyword: "lot"),
                CancellationToken.None);
            var serials = await handler.Handle(
                new ListInventoryDirectoryQuery(
                    "org-directory-pg",
                    "env-directory-pg",
                    InventoryDirectoryTypes.Serial,
                    SiteCode: "SITE-A",
                    SkuCode: "SKU-01",
                    Keyword: "sn-"),
                CancellationToken.None);

            Assert.Equal("LOC-A-01", Assert.Single(locations.Items).Code);
            Assert.Equal(
                InventoryDirectoryStableIds.Create(InventoryDirectoryTypes.Batch, "SKU-01", "LOT-001"),
                Assert.Single(batches.Items).Id);
            Assert.Equal(1, batches.Total);
            Assert.Equal(["SN-001", "SN-002"], serials.Items.Select(item => item.Code).ToArray());

            var batchPlan = await ExplainScopedLedgerQueryAsync(db, InventoryDirectoryTypes.Batch, "lot");
            var serialPlan = await ExplainScopedLedgerQueryAsync(db, InventoryDirectoryTypes.Serial, "sn-");
            Assert.Equal("on", await GetEnableSeqScanAsync(db));
            var scopedIndexName = await GetScopedLedgerIndexNameAsync(db);
            AssertNaturalScopedIndexPlan(batchPlan, scopedIndexName, InventoryDirectoryTypes.Batch);
            AssertNaturalScopedIndexPlan(serialPlan, scopedIndexName, InventoryDirectoryTypes.Serial);
        }
        finally
        {
            await DropInventorySchemaAsync(db);
        }
    }

    private sealed class DirectoryPostgresScope : IAsyncDisposable
    {
        private const string OwnerLabel = "nerv-iip.test.owner=man632-directory";
        private readonly string? containerName;
        private readonly string? volumeName;
        private readonly string? runLabel;

        private DirectoryPostgresScope(
            string connectionString,
            string? containerName = null,
            string? volumeName = null,
            string? runLabel = null)
        {
            ConnectionString = connectionString;
            this.containerName = containerName;
            this.volumeName = volumeName;
            this.runLabel = runLabel;
        }

        public string ConnectionString { get; }

        public static async Task<DirectoryPostgresScope> CreateAsync()
        {
            var external = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
            if (!string.IsNullOrWhiteSpace(external))
            {
                return new DirectoryPostgresScope(external);
            }

            var run = Guid.CreateVersion7().ToString("N");
            var container = $"nerv-iip-man632-pg-{run}";
            var volume = $"nerv-iip-man632-pg-data-{run}";
            var runLabel = $"nerv-iip.test.run={run}";
            var scope = new DirectoryPostgresScope(string.Empty, container, volume, runLabel);
            try
            {
                await RunDockerAsync(["volume", "create", "--label", OwnerLabel, "--label", runLabel, volume]);
                await RunDockerAsync([
                    "run", "-d",
                    "--name", container,
                    "--label", OwnerLabel,
                    "--label", runLabel,
                    "-e", "POSTGRES_PASSWORD=man632-test-password",
                    "-e", "POSTGRES_DB=man632_inventory",
                    "-p", "127.0.0.1:0:5432",
                    "--mount", $"source={volume},target=/var/lib/postgresql",
                    "postgres:18"]);
                var portOutput = await RunDockerAsync(["port", container, "5432/tcp"]);
                var portText = portOutput.Trim().Split(':').Last();
                if (!int.TryParse(portText, out var port))
                {
                    throw new InvalidOperationException("Could not resolve Docker's run-scoped PostgreSQL port.");
                }
                var connectionString = new NpgsqlConnectionStringBuilder
                {
                    Host = "127.0.0.1",
                    Port = port,
                    Database = "man632_inventory",
                    Username = "postgres",
                    Password = "man632-test-password",
                    IncludeErrorDetail = true,
                    Timeout = 1,
                }.ConnectionString;
                await WaitUntilReadyAsync(connectionString);
                return new DirectoryPostgresScope(connectionString, container, volume, runLabel);
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (containerName is null || volumeName is null || runLabel is null)
            {
                return;
            }

            await RunDockerAsync(["rm", "-f", containerName], allowFailure: true);
            await RunDockerAsync(["volume", "rm", volumeName], allowFailure: true);
            var containers = await RunDockerAsync([
                "ps", "-a", "--filter", $"label={runLabel}", "--format", "{{.Names}}"]);
            var volumes = await RunDockerAsync([
                "volume", "ls", "--filter", $"label={runLabel}", "--format", "{{.Name}}"]);
            if (!string.IsNullOrWhiteSpace(containers) || !string.IsNullOrWhiteSpace(volumes))
            {
                throw new InvalidOperationException("Run-scoped PostgreSQL Docker resources remain after cleanup.");
            }
        }

        /// <summary>
        /// Real container startup: bounded polling of an observable fact (the instance accepts a
        /// connection). The connection string is passed as a sensitive value so a timeout never prints
        /// credentials.
        /// </summary>
        private static async Task WaitUntilReadyAsync(string connectionString)
        {
            await Eventually.WaitAsync(
                condition: "the run-scoped PostgreSQL instance accepts connections",
                observe: async token =>
                {
                    try
                    {
                        await using var connection = new NpgsqlConnection(connectionString);
                        await connection.OpenAsync(token);
                        return (Accepted: true, Failure: (Exception?)null);
                    }
                    catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
                    {
                        return (Accepted: false, Failure: ex);
                    }
                },
                isSatisfied: observation => observation.Accepted,
                describe: observation => observation.Accepted
                    ? "accepting connections"
                    : $"not ready yet: {observation.Failure?.GetType().Name}: {observation.Failure?.Message}",
                options: new EventuallyOptions(
                    Timeout: TimeSpan.FromSeconds(30),
                    PollInterval: TimeSpan.FromMilliseconds(500),
                    SensitiveValues: [connectionString]));
        }

        private static async Task<string> RunDockerAsync(IReadOnlyCollection<string> arguments, bool allowFailure = false)
        {
            var startInfo = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Docker CLI.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout;
            var error = await stderr;
            if (!allowFailure && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Docker CLI failed with exit code {process.ExitCode}: {error.Trim()}");
            }

            return output;
        }
    }

    private static void AddLedger(
        ApplicationDbContext db,
        string locationCode,
        string lotNo,
        string serialNo,
        string organizationId = "org-directory-pg",
        string environmentId = "env-directory-pg",
        string siteCode = "SITE-A",
        string skuCode = "SKU-01")
    {
        var ledger = StockLedger.Create(
            organizationId,
            environmentId,
            skuCode,
            "piece",
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            "unrestricted",
            "company",
            null);
        ledger.ApplyMovement(Domain.AggregatesModel.StockMovementAggregate.StockMovement.Post(
            organizationId,
            environmentId,
            "inbound",
            "directory-pg-test",
            $"DOC-{locationCode}",
            "1",
            $"IDEM-{locationCode}",
            skuCode,
            "piece",
            siteCode,
            locationCode,
            lotNo,
            serialNo,
            "unrestricted",
            "company",
            null,
            1m));
        db.StockLedgers.Add(ledger);
    }

    private static async Task<string> ExplainScopedLedgerQueryAsync(
        ApplicationDbContext db,
        string directoryType,
        string keyword)
    {
        var request = new ListInventoryDirectoryQuery(
            "org-directory-pg",
            "env-directory-pg",
            directoryType,
            SiteCode: "SITE-A",
            SkuCode: "SKU-01",
            Keyword: keyword,
            Skip: 0,
            Take: 20);
        var values = InventoryDirectoryEfQueries.BuildValues(db, request, directoryType);
        var countPlan = await ExplainAsync(InventoryDirectoryEfQueries.BuildCount(values));
        var pagePlan = await ExplainAsync(InventoryDirectoryEfQueries.BuildPage(values, request.Skip, request.Take));
        return $"COUNT PLAN:{Environment.NewLine}{countPlan}{Environment.NewLine}PAGE PLAN:{Environment.NewLine}{pagePlan}";
    }

    private static void AssertNaturalScopedIndexPlan(
        string plan,
        string scopedIndexName,
        string directoryType)
    {
        Assert.Contains("Index", plan, StringComparison.Ordinal);
        Assert.True(
            plan.Contains(scopedIndexName, StringComparison.Ordinal),
            $"Expected {directoryType} EXPLAIN to use {scopedIndexName}:{Environment.NewLine}{plan}");
    }

    private static async Task<string> ExplainAsync(IQueryable query)
    {
        await using var command = query.CreateDbCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }
        command.CommandText = "EXPLAIN (FORMAT TEXT) " + command.CommandText;
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<string> GetScopedLedgerIndexNameAsync(ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'inventory'
              AND tablename = 'stock_ledgers'
              AND indexdef LIKE '%(organization_id, environment_id, site_code, sku_code, expiry_date)%'
            """;
        return (string?)await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Scoped stock-ledger index is missing.");
    }

    private static async Task<string> GetEnableSeqScanAsync(ApplicationDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SHOW enable_seqscan";
        return (string?)await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PostgreSQL did not return enable_seqscan.");
    }

    private static async Task DropInventorySchemaAsync(ApplicationDbContext db)
    {
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(InventoryFacts.Schema);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }
}
