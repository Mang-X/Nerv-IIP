using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNetCore.CAP;
using DotNetCore.CAP.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;
using Nerv.IIP.Contracts.Maintenance;
using Npgsql;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #2968 的真实 PostgreSQL 证据（lane 成员 <c>maintenance-asset-unavailable-v2-postgres</c>）：用 Development 环境的
/// 生产组合（真实 Program 注册、UoW、CAP EF outbox、InMemory transport）经 HTTP 打 v1/v2 入口，回读工单表与
/// <c>cap."published"</c>，证明目录精确命中在数据库谓词层成立、v1+v2 双发与工单同事务、
/// 任一 outbox 失败时整体回滚、v1 零漂移。默认 skip；设置 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class MaintenanceAssetUnavailableV2PostgresTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";
    private const string ExactCode = "Planned-Maintenance_01";
    private const string V1LegacyTopic = "AssetUnavailableIntegrationEvent";
    private const string V2DevelopmentTopic = "nerv-iip.development.business-maintenance.maintenance.asset-unavailable.v2";
    private const string WorkOrderOpenedTopic = "MaintenanceWorkOrderOpenedIntegrationEvent";

    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task V2_exact_code_commits_work_order_with_v1_companion_and_v2_canonical_outbox_rows_in_one_transaction()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndSeedCatalogAsync(factory);
        await InitializeCapAsync(factory);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-exact", ExactCode));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var workOrderId = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("workOrderId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(workOrderId));

        await using var db = CreateDbContext();
        var workOrder = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync();
        Assert.Equal(workOrderId, workOrder.Id.ToString());
        Assert.True(workOrder.AssetUnavailable);
        Assert.Equal(ExactCode, workOrder.AssetUnavailableReason);
        Assert.NotNull(workOrder.AssetUnavailableFromUtc);

        var rows = await ReadOutboxAsync(db);
        Assert.Equal(
            new[] { WorkOrderOpenedTopic, V1LegacyTopic, V2DevelopmentTopic }.Order(StringComparer.Ordinal).ToArray(),
            rows.Select(x => x.Topic).Order(StringComparer.Ordinal).ToArray());
        var v1 = Assert.Single(rows, x => x.Topic == V1LegacyTopic).Envelope;
        var v2 = Assert.Single(rows, x => x.Topic == V2DevelopmentTopic).Envelope;
        Assert.Equal(1, Get(v1, "eventVersion").GetInt32());
        Assert.Equal(2, Get(v2, "eventVersion").GetInt32());
        Assert.Equal("maintenance", Get(v1, "sourceService").GetString());
        Assert.Equal("business-maintenance", Get(v2, "sourceService").GetString());
        Assert.Equal("maintenance.AssetUnavailable", Get(v1, "eventType").GetString());
        Assert.Equal(Get(v1, "eventType").GetString(), Get(v2, "eventType").GetString());
        Assert.NotEqual(Get(v1, "eventId").GetString(), Get(v2, "eventId").GetString());
        // 业务键只能从 envelope 自己的时间重算：timestamptz 只有微秒精度，从工单列回读的 fromUtc 已丢掉 100ns 位，
        // Linux 上 UtcNow 的 100ns 位非 0（macOS 恰好微秒对齐），用列值重算会假红。
        var v1FromUtc = DateTimeOffset.Parse(Get(Get(v1, "payload"), "fromUtc").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var expectedKey = $"asset-unavailable:{workOrder.Id}:{v1FromUtc:O}";
        Assert.Equal(expectedKey, Get(v1, "idempotencyKey").GetString());
        Assert.Equal(expectedKey, Get(v2, "idempotencyKey").GetString());
        Assert.Equal(Get(v1, "idempotencyKey").GetString(), Get(v2, "idempotencyKey").GetString());
        Assert.StartsWith($"asset-unavailable:{workOrder.Id}:", Get(v2, "idempotencyKey").GetString(), StringComparison.Ordinal);
        Assert.Equal(Get(v1, "occurredAtUtc").GetRawText(), Get(v2, "occurredAtUtc").GetRawText());
        Assert.Equal(Get(Get(v1, "payload"), "fromUtc").GetRawText(), Get(Get(v2, "payload"), "fromUtc").GetRawText());
        // 列值是微秒截断后的同一时刻。
        Assert.Equal(TruncateToMicroseconds(v1FromUtc), workOrder.AssetUnavailableFromUtc.Value.ToUniversalTime());
        Assert.Equal(Get(v1, "correlationId").GetString(), Get(v2, "correlationId").GetString());
        Assert.Equal(Get(v1, "causationId").GetString(), Get(v2, "causationId").GetString());
        Assert.Equal(("org-001", "env-dev", "operator-001"), (Get(v2, "organizationId").GetString(), Get(v2, "environmentId").GetString(), Get(v2, "actor").GetString()));
        Assert.Equal(ExactCode, Get(Get(v1, "payload"), "reason").GetString());
        Assert.Equal(ExactCode, Get(Get(v2, "payload"), "reasonCode").GetString());
        Assert.False(TryGet(Get(v2, "payload"), "reason", out _));
        Assert.Equal("DEV-CNC-01", Get(Get(v2, "payload"), "deviceAssetId").GetString());

        // 同一 key 重放：同一收据，工单与 outbox 都不再增加。
        var replay = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-exact", ExactCode));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(workOrderId, JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("workOrderId").GetString());
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
        Assert.Equal(3, (await ReadOutboxAsync(db)).Count);

        // 同一 key 换原因码：既有 create-intent 冲突语义，不合并为同一意图。
        var conflict = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-exact", null));
        Assert.False(conflict.IsSuccessStatusCode);
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
        Assert.Equal(3, (await ReadOutboxAsync(db)).Count);
    }

    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task V2_near_miss_cross_scope_or_free_text_codes_are_rejected_by_the_database_predicate_with_zero_rows()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndSeedCatalogAsync(factory);
        await InitializeCapAsync(factory);
        using var client = CreateClient(factory);
        string[] rejected =
        [
            " " + ExactCode,
            ExactCode + " ",
            ExactCode.ToLowerInvariant(),
            ExactCode.ToUpperInvariant(),
            "other-organization-code",
            "other-environment-code",
            "over temperature",
            string.Empty,
            "   ",
        ];

        foreach (var reasonCode in rejected)
        {
            var response = await client.PostAsJsonAsync(
                "/api/business/v2/maintenance/work-orders",
                V2Body($"v2-pg-reject-{Guid.CreateVersion7():N}", reasonCode));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"'{reasonCode}' -> {(int)response.StatusCode}: {body}");
            if (reasonCode.Trim().Length > 0)
            {
                // 稳定错误码不回显用户输入。
                Assert.DoesNotContain(reasonCode.Trim(), body, StringComparison.Ordinal);
            }
        }

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.MaintenanceWorkOrders.CountAsync());
        Assert.Equal(0, await db.CodeIdempotencyKeys.CountAsync());
        Assert.Empty(await ReadOutboxAsync(db));

        // 阳性对照：同一进程、同一目录，精确值立即命中——证明上面九个拒绝不是「什么都拒绝」。
        var accepted = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-accept", ExactCode));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, await db.MaintenanceWorkOrders.CountAsync());
    }

    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task V2_outbox_failure_rolls_back_the_work_order_and_the_already_published_v1_companion()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IMaintenanceIntegrationEventOutboxPublisher>();
            services.AddScoped<IMaintenanceIntegrationEventOutboxPublisher>(sp =>
                new FailOnTopicOutboxPublisher(
                    new CapMaintenanceIntegrationEventOutboxPublisher(sp),
                    topic => AssetUnavailableIntegrationEventTopics.TryParseV2(topic, out _)));
        });
        await MigrateAndSeedCatalogAsync(factory);
        await InitializeCapAsync(factory);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-rollback", ExactCode));

        Assert.False(response.IsSuccessStatusCode);
        var decorator = FailOnTopicOutboxPublisher.Last;
        Assert.NotNull(decorator);
        // v1 companion 已经真的经 CAP 进了同一事务（不是被跳过），随后 v2 写入失败让整笔事务回滚。
        Assert.Equal([V1LegacyTopic], decorator.DelegatedTopics);
        Assert.Equal(V2DevelopmentTopic, decorator.RejectedTopic);

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.MaintenanceWorkOrders.CountAsync());
        Assert.Equal(0, await db.CodeIdempotencyKeys.CountAsync());
        Assert.Empty(await ReadOutboxAsync(db));
    }

    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task V1_companion_outbox_failure_rolls_back_the_work_order_before_the_v2_envelope_is_attempted()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IMaintenanceIntegrationEventOutboxPublisher>();
            services.AddScoped<IMaintenanceIntegrationEventOutboxPublisher>(sp =>
                new FailOnTopicOutboxPublisher(
                    new CapMaintenanceIntegrationEventOutboxPublisher(sp),
                    topic => topic == V1LegacyTopic));
        });
        await MigrateAndSeedCatalogAsync(factory);
        await InitializeCapAsync(factory);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-rollback-v1", ExactCode));

        Assert.False(response.IsSuccessStatusCode);
        var decorator = FailOnTopicOutboxPublisher.Last;
        Assert.NotNull(decorator);
        Assert.Equal(V1LegacyTopic, decorator.RejectedTopic);
        // v1 companion 先失败，v2 canonical 根本不会尝试：不存在「只发了 v2」的半次双发。
        Assert.Empty(decorator.DelegatedTopics);

        await using var db = CreateDbContext();
        Assert.Equal(0, await db.MaintenanceWorkOrders.CountAsync());
        Assert.Equal(0, await db.CodeIdempotencyKeys.CountAsync());
        Assert.Empty(await ReadOutboxAsync(db));
    }

    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task V2_null_reason_code_commits_a_plain_work_order_without_asset_unavailable_outbox_rows()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndSeedCatalogAsync(factory);
        await InitializeCapAsync(factory);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/business/v2/maintenance/work-orders", V2Body("v2-pg-null", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = CreateDbContext();
        var workOrder = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync();
        Assert.False(workOrder.AssetUnavailable);
        Assert.Null(workOrder.AssetUnavailableReason);
        var rows = await ReadOutboxAsync(db);
        Assert.Equal([WorkOrderOpenedTopic], rows.Select(x => x.Topic).ToArray());
    }

    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task V1_free_text_still_publishes_only_the_v1_envelope_without_touching_the_catalog()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndSeedCatalogAsync(factory);
        await InitializeCapAsync(factory);
        using var client = CreateClient(factory);

        // 不在目录里的自由文本、带前后空格：v1 既有 trim 语义照旧，不查目录、不把它解释为码。
        var response = await client.PostAsJsonAsync(
            "/api/business/v1/maintenance/work-orders",
            new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                deviceAssetId = "DEV-CNC-01",
                priority = "high",
                sourceAlarmId = (string?)null,
                openedBy = "operator-001",
                assetUnavailableReason = "  not-a-catalog-code  ",
                idempotencyKey = "v1-pg-free-text",
            });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var db = CreateDbContext();
        var workOrder = await db.MaintenanceWorkOrders.AsNoTracking().SingleAsync();
        Assert.True(workOrder.AssetUnavailable);
        Assert.Equal("not-a-catalog-code", workOrder.AssetUnavailableReason);
        var rows = await ReadOutboxAsync(db);
        Assert.Equal(
            new[] { WorkOrderOpenedTopic, V1LegacyTopic }.Order(StringComparer.Ordinal).ToArray(),
            rows.Select(x => x.Topic).Order(StringComparer.Ordinal).ToArray());
        var v1 = Assert.Single(rows, x => x.Topic == V1LegacyTopic).Envelope;
        Assert.Equal(1, Get(v1, "eventVersion").GetInt32());
        Assert.Equal("maintenance", Get(v1, "sourceService").GetString());
        Assert.Equal("not-a-catalog-code", Get(Get(v1, "payload"), "reason").GetString());
        Assert.DoesNotContain(rows, x => x.Topic == V2DevelopmentTopic);
    }

    /// <summary>
    /// #3855：停机原因产品基线 seed 默认开启，但只在库结构迁移到当前版本之后执行。宿主在迁移前启动时跳过
    /// （不能在缺表的库上写数据把启动打崩），迁移完成后的下一次启动补齐 12 条。
    /// </summary>
    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task Baseline_downtime_reason_seed_waits_for_migrations_and_fills_the_catalog_on_the_next_start()
    {
        await ResetMaintenanceSchemaAsync();
        var startupLogs = new CapturingLoggerProvider();
        await using (var beforeMigration = CreateFactory(services => services.AddSingleton<ILoggerProvider>(startupLogs)))
        {
            // 宿主必须真的起得来：发一次 HTTP 请求（取 Services 不够——启动抛异常时它照样返回）。
            using var client = CreateClient(beforeMigration);
            // 库还没迁移，选一个不碰库的端点证明宿主已在服务请求。
            var probe = await client.GetAsync("/swagger/v1/swagger.json");
            Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
            Assert.Contains(startupLogs.Messages, message => message.StartsWith("Maintenance product seed skipped:", StringComparison.Ordinal));
            Assert.DoesNotContain(startupLogs.Messages, message => message.StartsWith("Maintenance product seed completed:", StringComparison.Ordinal));

            using var scope = beforeMigration.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AssertUsesGovernedDatabase(db);
            Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync());
            await db.Database.MigrateAsync();
            Assert.False(await db.DowntimeReasons.AnyAsync());
        }

        await using var afterMigration = CreateFactory();
        using var afterScope = afterMigration.Services.CreateScope();
        var migrated = afterScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var codes = await migrated.DowntimeReasons.AsNoTracking()
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .Select(x => x.ReasonCode)
            .ToListAsync();
        Assert.Equal(
            DowntimeReasonBaselineSeedService.Reasons.Select(x => x.Code).Order(StringComparer.Ordinal),
            codes.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// #3852：维修工单号迁移给存量工单补号——演示数据已有的 MWO- 单号原样沿用，其余按开单日补
    /// MWO-yyyyMMdd-NNNNNN，并把当日计数器推到已用的最大序号，之后分配的新号不会与补出的号相撞。
    /// </summary>
    [MaintenanceAssetUnavailableV2PostgresFact]
    public async Task Work_order_number_migration_backfills_existing_rows_and_advances_the_counter()
    {
        await ResetMaintenanceSchemaAsync();
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        AssertUsesGovernedDatabase(db);
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20260928075938_AddSparePartIssueLocation");

        var today = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO maintenance.maintenance_work_orders
                (id, organization_id, environment_id, device_asset_id, priority, opened_by, status,
                 opened_at_utc, alarm_cleared, asset_unavailable, version, source_type, source_reference_id)
            VALUES
                ('01a0e700-0000-7000-8000-000000000001', 'org-001', 'env-dev', 'DEV-1', 'high', 'seed', 'Open',
                 {0}, false, false, 0, 'alarm', 'MWO-2026-0042'),
                ('01a0e700-0000-7000-8000-000000000002', 'org-001', 'env-dev', 'DEV-1', 'high', 'seed', 'Open',
                 {1}, false, false, 0, 'manual', NULL),
                ('01a0e700-0000-7000-8000-000000000003', 'org-001', 'env-dev', 'DEV-1', 'high', 'seed', 'Open',
                 {2}, false, false, 0, 'plan', 'PM-0001:date:20260901'),
                ('01a0e700-0000-7000-8000-000000000004', 'org-001', 'env-dev', 'DEV-1', 'high', 'seed', 'Open',
                 {3}, false, false, 0, 'manual', NULL);
            """,
            today.AddMinutes(-30),
            today.AddMinutes(-20),
            today.AddMinutes(-10),
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));

        await migrator.MigrateAsync();

        var numbers = await db.MaintenanceWorkOrders.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => x.WorkOrderNo)
            .ToListAsync();
        var day = today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(
            ["MWO-2026-0042", $"MWO-{day}-000001", $"MWO-{day}-000002", "MWO-20260901-000001"],
            numbers);

        // 计数器已推到当日已用的最大序号：新分配的单号从 000003 开始，不与补出的号相撞。
        var coding = scope.ServiceProvider.GetRequiredService<Nerv.IIP.Business.Maintenance.Web.Application.Commands.MaintenanceCodingService>();
        var next = await Nerv.IIP.Business.Maintenance.Web.Application.Commands.MaintenanceWorkOrderNumbers.AllocateAsync(
            coding, "org-001", "env-dev", intentKey: null, CancellationToken.None);
        Assert.Equal($"MWO-{day}-000003", next);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages => messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = LaneConnectionString,
                    ["Messaging:Provider"] = "InMemory",
                    ["Cap:Version"] = "t-2968-v2",
                    ["IndustrialTelemetry:BaseUrl"] = "http://industrial-telemetry.local",
                    ["InternalService:BearerToken"] = "test-internal-token",
                };
                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }

                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        return client;
    }

    private static object V2Body(string idempotencyKey, string? reasonCode) => new
    {
        organizationId = "org-001",
        environmentId = "env-dev",
        deviceAssetId = "DEV-CNC-01",
        priority = "high",
        sourceAlarmId = (string?)null,
        openedBy = "operator-001",
        assetUnavailableReasonCode = reasonCode,
        idempotencyKey,
    };

    private static async Task MigrateAndSeedCatalogAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        db.DowntimeReasons.AddRange(
            DowntimeReason.Create("org-001", "env-dev", ExactCode, "Planned maintenance"),
            DowntimeReason.Create("org-002", "env-dev", "other-organization-code", "Other organization"),
            DowntimeReason.Create("org-001", "env-prod", "other-environment-code", "Other environment"));
        await db.SaveChangesAsync();
    }

    private static async Task InitializeCapAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(LaneConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MaintenanceFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static async Task<List<(string Topic, JsonElement Envelope)>> ReadOutboxAsync(ApplicationDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        // 生产 CAP 存储（DotNetCore.CAP.PostgreSql，Program 的 UseEntityFramework）把 outbox 落在 cap."published"；
        // EF 模型里的 maintenance."CAPPublishedMessage" 只是 netcorepal 映射的表结构，运行时不写入。
        command.CommandText = """SELECT "Name", "Content" FROM cap."published" ORDER BY "Id";""";
        var rows = new List<(string, JsonElement)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var content = JsonDocument.Parse(reader.GetString(1)).RootElement.Clone();
            rows.Add((reader.GetString(0), Get(content, "value")));
        }

        return rows;
    }

    private static JsonElement Get(JsonElement element, string propertyName) =>
        TryGet(element, propertyName, out var value)
            ? value
            : throw new KeyNotFoundException($"Property '{propertyName}' was not found in {element.GetRawText()}");

    private static bool TryGet(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string LaneConnectionString =>
        Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"{PostgresConnectionStringEnvironmentVariable} must be set for the Maintenance AssetUnavailable v2 PostgreSQL tests.");

    private static async Task ResetMaintenanceSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(LaneConnectionString);
        await connection.OpenAsync();
        foreach (var schema in new[] { MaintenanceFacts.Schema, "cap" })
        {
            var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void AssertUsesGovernedDatabase(ApplicationDbContext dbContext)
    {
        var governed = new NpgsqlConnectionStringBuilder(LaneConnectionString);
        var observed = new NpgsqlConnectionStringBuilder(dbContext.Database.GetDbConnection().ConnectionString);
        Assert.Equal(
            (governed.Host, governed.Port, governed.Database),
            (observed.Host, observed.Port, observed.Database));
    }

    /// <summary>被拒绝的 topic 抛错，其余 topic 真的经 CAP 进同一事务——模拟双发中任一条 outbox 写入失败。</summary>
    private sealed class FailOnTopicOutboxPublisher(IMaintenanceIntegrationEventOutboxPublisher inner, Func<string, bool> reject)
        : IMaintenanceIntegrationEventOutboxPublisher
    {
        public static FailOnTopicOutboxPublisher? Last { get; private set; }

        public List<string> DelegatedTopics { get; } = [];

        public string? RejectedTopic { get; private set; }

        public async Task PublishAsync<T>(string topic, T integrationEvent, CancellationToken cancellationToken)
        {
            Last = this;
            if (reject(topic))
            {
                RejectedTopic = topic;
                throw new InvalidOperationException($"Simulated outbox failure for '{topic}'.");
            }

            DelegatedTopics.Add(topic);
            await inner.PublishAsync(topic, integrationEvent, cancellationToken);
        }
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);

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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MaintenanceAssetUnavailableV2PostgresFactAttribute : FactAttribute
    {
        public MaintenanceAssetUnavailableV2PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)))
            {
                Skip = $"Set {PostgresConnectionStringEnvironmentVariable} to run the real PostgreSQL Maintenance AssetUnavailable v2 tests.";
            }
        }
    }
}
