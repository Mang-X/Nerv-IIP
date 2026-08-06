using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Queries;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Endpoints.Iiot;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ConnectorTagManifestTests
{
    private const string RevisionA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string RevisionB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Canonical_revision_matches_the_cross_solution_fixed_vector_and_excludes_activation_facts()
    {
        ReportConnectorTagManifestEntry[] entries =
        [
            Entry(" DEV-B ", " Temperature ", enabled: true, protocolAddress: " ns=2;s=Temperature ", activationStatus: "error", activationObservedAtUtc: Now, errorCode: "E-1", errorMessage: "first failure"),
            Entry("DEV-A", "pressure", enabled: false, protocolAddress: null, activationStatus: "disabled", activationObservedAtUtc: Now),
        ];
        var changedActivation = entries
            .Select(entry => entry with
            {
                ActivationStatus = "active",
                ActivationObservedAtUtc = entry.ActivationObservedAtUtc.AddMinutes(1),
                ActivationErrorCode = null,
                ActivationErrorMessage = null,
            })
            .Reverse()
            .ToArray();

        var revision = ConnectorTagManifestRevision.Compute(" OPCUA ", entries);
        var activationOnlyRevision = ConnectorTagManifestRevision.Compute("opcua", changedActivation);

        Assert.Equal("e0ff8c1111083580a719587480101437f3fcd5bf76bb822fc3ae5f2698631e44", revision);
        Assert.Equal(revision, activationOnlyRevision);
    }

    [Fact]
    public void Canonical_utf8_bytes_and_hash_are_frozen_for_unicode_and_json_escape_characters()
    {
        ReportConnectorTagManifestEntry[] entries =
        [
            Entry(
                " DEV-中文😀-\"\\\n\u0001 ",
                " TAG-\"\\\n\u0002 ",
                protocolAddress: " ns=2;s=中文😀-\"\\\n\u0003 ",
                activationObservedAtUtc: Now),
        ];
        const string expectedJson = """{"sourceSystem":"opcua-\u4E2D\u6587\uD83D\uDE00","entries":[{"deviceAssetId":"DEV-\u4E2D\u6587\uD83D\uDE00-\u0022\\\n\u0001","tagKey":"tag-\u0022\\\n\u0002","enabled":true,"protocolAddress":"ns=2;s=\u4E2D\u6587\uD83D\uDE00-\u0022\\"}]}""";

        var canonicalBytes = ConnectorTagManifestRevision.ComputeCanonicalUtf8Bytes(" OPCUA-中文😀 ", entries);
        var revision = ConnectorTagManifestRevision.Compute(" OPCUA-中文😀 ", entries);

        Assert.All(canonicalBytes, value => Assert.InRange(value, (byte)0, (byte)127));
        Assert.Equal(expectedJson, Encoding.UTF8.GetString(canonicalBytes));
        Assert.Equal("e047382c6f4bb10de8f61e5fd1112ee46805620f5749528c16ace0b923d8ae71", revision);
        Assert.Equal(revision, Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant());
    }

    [Fact]
    public void Persistent_versions_are_concurrency_tokens_and_observation_times_are_business_facts_only()
    {
        using var dbContext = CreateDbContext();

        var manifestType = dbContext.Model.FindEntityType(typeof(ConnectorTagManifest))!;
        var bindingType = dbContext.Model.FindEntityType(typeof(ConnectorTagBinding))!;
        var manifestVersion = manifestType.FindProperty(nameof(ConnectorTagManifest.ConcurrencyVersion))!;
        var bindingVersion = bindingType.FindProperty(nameof(ConnectorTagBinding.ConcurrencyVersion))!;
        var manifestObservedAt = manifestType.FindProperty(nameof(ConnectorTagManifest.ManifestObservedAtUtc))!;
        var activationObservedAt = bindingType.FindProperty(nameof(ConnectorTagBinding.ActivationObservedAtUtc))!;

        Assert.True(manifestVersion.IsConcurrencyToken);
        Assert.True(bindingVersion.IsConcurrencyToken);
        Assert.False(manifestObservedAt.IsConcurrencyToken);
        Assert.False(activationObservedAt.IsConcurrencyToken);
    }

    [Fact]
    public async Task Report_command_recomputes_revision_and_applies_activation_by_its_independent_observation()
    {
        await using var dbContext = CreateDbContext();
        var pending = Entry("DEV-CNC-01", "temperature", activationStatus: "pending", activationObservedAtUtc: Now);
        var revision = ConnectorTagManifestRevision.Compute("opcua", [pending]);
        var handler = CreateReportHandler(dbContext);

        var accepted = await handler.Handle(Command(revision, Now, [pending]), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var idempotent = await handler.Handle(Command(revision, Now,
        [
            pending with
            {
                ActivationStatus = "active",
                ActivationObservedAtUtc = Now.AddMinutes(1),
            },
        ]), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(ManifestApplyDisposition.Accepted, accepted.Disposition);
        Assert.Equal(ManifestApplyDisposition.Idempotent, idempotent.Disposition);
        var manifest = await dbContext.ConnectorTagManifests.Include(x => x.Bindings).SingleAsync();
        var binding = Assert.Single(manifest.Bindings);
        Assert.Equal("active", binding.ActivationStatus);
        Assert.Equal(Now.AddMinutes(1), binding.ActivationObservedAtUtc);
    }

    [Fact]
    public async Task Report_command_rejects_a_revision_that_does_not_match_the_server_canonical_shape()
    {
        await using var dbContext = CreateDbContext();
        var entry = Entry("DEV-CNC-01", "temperature", activationObservedAtUtc: Now);
        var handler = CreateReportHandler(dbContext);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            Command(RevisionA, Now, [entry]),
            CancellationToken.None));

        Assert.Contains("canonical", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.ConnectorTagManifests);
    }

    [Fact]
    public async Task Report_command_rejects_source_observations_beyond_the_configured_five_minute_clock_skew()
    {
        await using var dbContext = CreateDbContext();
        var entry = Entry("DEV-CNC-01", "temperature", activationObservedAtUtc: Now);
        var revision = ConnectorTagManifestRevision.Compute("opcua", [entry]);
        var handler = CreateReportHandler(dbContext);

        var boundary = await handler.Handle(Command(revision, Now.AddMinutes(5),
        [
            entry with { ActivationObservedAtUtc = Now.AddMinutes(5) },
        ]), CancellationToken.None);
        var futureEntry = entry with { ActivationObservedAtUtc = Now.AddMinutes(5).AddTicks(1) };
        var futureRevision = ConnectorTagManifestRevision.Compute("opcua", [futureEntry]);
        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            Command(futureRevision, Now.AddMinutes(5).AddTicks(1), [futureEntry]),
            CancellationToken.None));

        Assert.Equal(ManifestApplyDisposition.Accepted, boundary.Disposition);
        Assert.Contains("future", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Report_command_returns_the_accepted_revision_and_observation_for_stale_and_conflicting_reports()
    {
        await using var dbContext = CreateDbContext();
        var first = Entry("DEV-CNC-01", "temperature", activationObservedAtUtc: Now);
        var firstRevision = ConnectorTagManifestRevision.Compute("opcua", [first]);
        var replacement = Entry("DEV-CNC-01", "pressure", activationObservedAtUtc: Now);
        var replacementRevision = ConnectorTagManifestRevision.Compute("opcua", [replacement]);
        var handler = CreateReportHandler(dbContext);
        await handler.Handle(Command(firstRevision, Now, [first]), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var stale = await handler.Handle(Command(replacementRevision, Now.AddTicks(-1), [replacement]), CancellationToken.None);
        var conflict = await handler.Handle(Command(replacementRevision, Now, [replacement]), CancellationToken.None);

        Assert.Equal(ManifestApplyDisposition.Stale, stale.Disposition);
        Assert.Equal(ManifestApplyDisposition.Conflict, conflict.Disposition);
        Assert.Equal(firstRevision, stale.AcceptedManifestRevision);
        Assert.Equal(firstRevision, conflict.AcceptedManifestRevision);
        Assert.Equal(Now, stale.AcceptedManifestObservedAtUtc);
        Assert.Equal(Now, conflict.AcceptedManifestObservedAtUtc);
    }

    [Fact]
    public void Report_validator_rejects_invalid_scope_hash_status_duplicate_normalized_keys_and_error_bounds()
    {
        var duplicate = Entry(" DEV-CNC-01 ", " Temperature ", activationObservedAtUtc: Now);
        var invalid = new ReportConnectorTagManifestCommand(
            "",
            new string('e', 101),
            new string('c', 151),
            new string('s', 101),
            RevisionA.ToUpperInvariant(),
            Now,
            [
                duplicate,
                duplicate with
                {
                    DeviceAssetId = "DEV-CNC-01",
                    TagKey = "temperature",
                    ActivationStatus = "unknown",
                    ActivationErrorCode = new string('x', 129),
                    ActivationErrorMessage = new string('y', 501),
                },
            ]);

        var result = new ReportConnectorTagManifestCommandValidator().Validate(invalid);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => SameProperty(error.PropertyName, nameof(ReportConnectorTagManifestCommand.OrganizationId)));
        Assert.Contains(result.Errors, error => SameProperty(error.PropertyName, nameof(ReportConnectorTagManifestCommand.ManifestRevision)));
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => SamePropertySuffix(error.PropertyName, nameof(ReportConnectorTagManifestEntry.ActivationStatus)));
        Assert.Contains(result.Errors, error => SamePropertySuffix(error.PropertyName, nameof(ReportConnectorTagManifestEntry.ActivationErrorCode)));
        Assert.Contains(result.Errors, error => SamePropertySuffix(error.PropertyName, nameof(ReportConnectorTagManifestEntry.ActivationErrorMessage)));
    }

    [Fact]
    public void Report_validators_reject_manifests_above_the_bounded_entry_limit()
    {
        var entries = Enumerable.Range(0, ConnectorTagManifestLimits.MaxEntries + 1)
            .Select(index => Entry("DEV-CNC-01", $"tag-{index}", activationObservedAtUtc: Now))
            .ToArray();
        var command = new ReportConnectorTagManifestCommand(
            "org-001", "env-dev", "connector-a", "opcua", RevisionA, Now, entries);
        var request = new ReportConnectorTagManifestRequest(
            "org-001", "env-dev", "connector-a", "opcua", RevisionA, Now, entries);

        var commandResult = new ReportConnectorTagManifestCommandValidator().Validate(command);
        var requestResult = new ReportConnectorTagManifestRequestValidator().Validate(request);

        Assert.Contains(commandResult.Errors, error => error.ErrorMessage.Contains("more than 500", StringComparison.Ordinal));
        Assert.Contains(requestResult.Errors, error => error.ErrorMessage.Contains("more than 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Coverage_query_keeps_never_sampled_bindings_isolates_connectors_and_excludes_retired_bindings()
    {
        await using var dbContext = CreateDbContext();
        var temperature = DomainEntry("DEV-CNC-01", "temperature", enabled: true, activationStatus: "active");
        var pressure = DomainEntry("DEV-CNC-01", "pressure", enabled: true, activationStatus: "error", errorCode: "subscription-failed");
        var disabled = DomainEntry("DEV-CNC-02", "vibration", enabled: false, activationStatus: "disabled");
        var retired = DomainEntry("DEV-CNC-03", "energy", enabled: true, activationStatus: "active");
        var manifest = ConnectorTagManifest.Create("org-001", "env-dev", "connector-a", "opcua", RevisionA, Now, [temperature, pressure, disabled, retired]);
        manifest.Apply("opcua", RevisionB, Now.AddMinutes(1), [temperature, pressure, disabled]);
        dbContext.ConnectorTagManifests.Add(manifest);
        dbContext.TelemetrySummaries.AddRange(
            Summary("connector-a", "DEV-CNC-01", "temperature", Now.AddHours(-3), Now.AddHours(-2), "a-1"),
            Summary("connector-a", "DEV-CNC-01", "temperature", Now.AddHours(-1), Now, "a-2"),
            Summary("connector-b", "DEV-CNC-01", "temperature", Now.AddDays(-1), Now.AddHours(1), "b-1"),
            TelemetrySummary.Record("org-other", "env-dev", "DEV-CNC-01", "temperature", Now.AddDays(-2), Now.AddHours(2), 1, 1m, 1m, 1m, "other-1", "opcua", "source", "connector-a"));
        await dbContext.SaveChangesAsync();

        var result = await new GetConnectorTagCoverageQueryHandler(dbContext).Handle(
            new GetConnectorTagCoverageQuery("org-001", "env-dev", "connector-a"),
            CancellationToken.None);

        Assert.Equal("current", result.ManifestStatus);
        Assert.Equal(3, result.ConfiguredCount);
        Assert.Equal(2, result.EnabledCount);
        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(1, result.EverSampledCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.DoesNotContain(result.Items, item => item.TagKey == "energy");
        var sampled = Assert.Single(result.Items, item => item.TagKey == "temperature");
        Assert.Equal(Now.AddHours(-3), sampled.FirstSampleAtUtc);
        Assert.Equal(Now, sampled.LastSampleAtUtc);
        var neverSampled = Assert.Single(result.Items, item => item.TagKey == "pressure");
        Assert.Null(neverSampled.FirstSampleAtUtc);
        Assert.Null(neverSampled.LastSampleAtUtc);
    }

    [Fact]
    public async Task Coverage_query_distinguishes_an_unavailable_manifest_from_a_current_empty_manifest()
    {
        await using var dbContext = CreateDbContext();
        var handler = new GetConnectorTagCoverageQueryHandler(dbContext);

        var unavailable = await handler.Handle(
            new GetConnectorTagCoverageQuery("org-001", "env-dev", "connector-empty"),
            CancellationToken.None);
        dbContext.ConnectorTagManifests.Add(ConnectorTagManifest.Create(
            "org-001", "env-dev", "connector-empty", "mqtt", RevisionA, Now, []));
        await dbContext.SaveChangesAsync();
        var currentEmpty = await handler.Handle(
            new GetConnectorTagCoverageQuery("org-001", "env-dev", "connector-empty"),
            CancellationToken.None);

        Assert.Equal("unavailable", unavailable.ManifestStatus);
        Assert.Null(unavailable.ManifestRevision);
        Assert.Empty(unavailable.Items);
        Assert.Equal("current", currentEmpty.ManifestStatus);
        Assert.Equal(RevisionA, currentEmpty.ManifestRevision);
        Assert.Empty(currentEmpty.Items);
        Assert.Equal(0, currentEmpty.ConfiguredCount);
    }

    [Fact]
    public void Coverage_projection_translates_for_npgsql_and_uses_only_the_full_key_summary_join()
    {
        // 这条用例的被测意图是**翻译**，不是网络行为：EF 在建立连接之前就完成 SQL 生成，
        // `ToQueryString()` 从头到尾不拨号。因此这里刻意**不**使用 NetworkFailureFixture ——
        // 预留一个真实端口只会带来无谓的端口竞态，却不会被这条用例的任何一行断言触及；给它套一句
        // `Assert.Equal(ConnectionRefused, ...)` 更是假断言，因为根本没有连接尝试可供分类。
        //
        // 「不拨号」不靠注释自证，也不靠 `GetDbConnection().State`：连接用完即关，那个断言在「从未
        // 拨号」和「拨过又关了」两种情况下都为真，是恒真断言。这里改为拦截**拨号动作本身**并断言
        // 次数为 0 —— 一旦翻译退化成会执行的路径，计数就 > 0 而变红。主机名仍用 RFC 2606 保留的
        // `.invalid` 顶级域兜底：万一真去连库，得到的是立即的解析失败而不是静默连上某台真机。
        var connectionAttempts = new ConnectionOpenCountingInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=translation-only.invalid;Port=5432;Database=translation_only;Username=nerv;Password=nerv")
            .AddInterceptors(connectionAttempts)
            .Options;
        using var dbContext = new ApplicationDbContext(options, new NoopMediator());

        var sql = ConnectorTagCoverageQueryProjection.Build(
                dbContext,
                new GetConnectorTagCoverageQuery("org-001", "env-dev", "connector-a"))
            .ToQueryString();

        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connector_tag_bindings", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("telemetry_summaries", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("organization_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("environment_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collection_connector_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("device_asset_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag_key", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telemetry_raw_samples", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device_control", sql, StringComparison.OrdinalIgnoreCase);

        // 被测意图的最后一环：整段翻译没有触碰过网络。这条断言一旦失败，说明查询构造退化成了会
        // 拨号的路径，那时才需要谈网络失败分类 —— 在此之前谈它属于装饰。
        Assert.Equal(0, connectionAttempts.OpenAttempts);
    }

    [Fact]
    public void Connection_open_counting_interceptor_actually_observes_a_dial()
    {
        // 上一条用例断言计数为 0；这条是它的正向对照，防止「计数器压根没接上 EF」把那个断言变成
        // 又一个恒真断言。用 SQLite 内存库拨号：走的是同一条 EF relational 连接拦截路径，却完全
        // 不碰网络与文件系统。
        var connectionAttempts = new ConnectionOpenCountingInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(connectionAttempts)
            .Options;
        using var dbContext = new ApplicationDbContext(options, new NoopMediator());

        Assert.Equal(0, connectionAttempts.OpenAttempts);

        dbContext.Database.OpenConnection();
        try
        {
            Assert.Equal(1, connectionAttempts.OpenAttempts);
        }
        finally
        {
            dbContext.Database.CloseConnection();
        }
    }

    [Theory]
    [InlineData("POST", "/api/business/v1/iiot/connector-tag-manifests")]
    [InlineData("GET", "/api/business/v1/iiot/connectors/connector-a/tag-coverage?organizationId=org-001&environmentId=env-dev")]
    public async Task Manifest_endpoints_reject_anonymous_callers(string method, string route)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new
            {
                organizationId = "org-001",
                environmentId = "env-dev",
                collectionConnectorId = "connector-a",
                sourceSystem = "opcua",
                manifestRevision = RevisionA,
                manifestObservedAtUtc = Now,
                entries = Array.Empty<object>(),
            });
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static ReportConnectorTagManifestCommandHandler CreateReportHandler(ApplicationDbContext dbContext)
    {
        return new ReportConnectorTagManifestCommandHandler(
            dbContext,
            new FixedTimeProvider(Now),
            Options.Create(new ConnectorTagManifestIngestionOptions
            {
                MaxFutureObservationSkew = TimeSpan.FromMinutes(5),
            }));
    }

    private static ReportConnectorTagManifestCommand Command(
        string revision,
        DateTimeOffset observedAtUtc,
        IReadOnlyCollection<ReportConnectorTagManifestEntry> entries)
    {
        return new ReportConnectorTagManifestCommand(
            "org-001",
            "env-dev",
            "connector-a",
            "opcua",
            revision,
            observedAtUtc,
            entries);
    }

    private static ReportConnectorTagManifestEntry Entry(
        string deviceAssetId,
        string tagKey,
        bool enabled = true,
        string? protocolAddress = "ns=2;s=value",
        string activationStatus = "active",
        DateTimeOffset? activationObservedAtUtc = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        return new ReportConnectorTagManifestEntry(
            deviceAssetId,
            tagKey,
            enabled,
            protocolAddress,
            activationStatus,
            activationObservedAtUtc ?? Now,
            errorCode,
            errorMessage);
    }

    private static ConnectorTagManifestEntry DomainEntry(
        string deviceAssetId,
        string tagKey,
        bool enabled,
        string activationStatus,
        string? errorCode = null)
    {
        return new ConnectorTagManifestEntry(
            deviceAssetId,
            tagKey,
            enabled,
            $"ns=2;s={tagKey}",
            activationStatus,
            Now,
            errorCode,
            errorCode is null ? null : "activation failed");
    }

    private static TelemetrySummary Summary(
        string connectorId,
        string deviceAssetId,
        string tagKey,
        DateTimeOffset bucketStartUtc,
        DateTimeOffset bucketEndUtc,
        string sequence)
    {
        return TelemetrySummary.Record(
            "org-001",
            "env-dev",
            deviceAssetId,
            tagKey,
            bucketStartUtc,
            bucketEndUtc,
            1,
            1m,
            1m,
            1m,
            sequence,
            "opcua",
            "source",
            connectorId);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"connector-tag-manifest-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static bool SameProperty(string actual, string expected)
    {
        return string.Equals(
            actual.Replace(" ", string.Empty, StringComparison.Ordinal),
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePropertySuffix(string actual, string expected)
    {
        return actual.Replace(" ", string.Empty, StringComparison.Ordinal)
            .EndsWith(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 计数 EF 尝试打开数据库连接的次数。刻意计的是 <c>ConnectionOpening</c>（拨号动作）而不是
    /// 事后的 <c>DbConnection.State</c>：后者用完即关，无法区分「从未拨号」与「拨过又关了」。
    /// </summary>
    private sealed class ConnectionOpenCountingInterceptor : DbConnectionInterceptor
    {
        private int openAttempts;

        public int OpenAttempts => Volatile.Read(ref openAttempts);

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
        {
            Interlocked.Increment(ref openAttempts);
            return base.ConnectionOpening(connection, eventData, result);
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref openAttempts);
            return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
