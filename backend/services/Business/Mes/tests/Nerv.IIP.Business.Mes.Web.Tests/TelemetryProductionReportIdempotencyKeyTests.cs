using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.AspNetCore.Validation;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Coding;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 遥测报工派生幂等键的有界化（#3477）。
/// </summary>
public sealed class TelemetryProductionReportIdempotencyKeyTests
{
    // 本仓自己的命名约定，逐段都有 producer，不是极端输入：
    // - org / env：本仓夹具与 seed 的默认租户作用域；
    // - DEV-CNC-01：WorldBibleSpec 的设备编码段（`DEV-CNC-01..10`）；
    // - parts_count：本目录 TelemetryProductionReportAutomationTests 夹具用的产量 tag；
    // - seed-world-history / CONN-OPCUA-01：WorldHistoryDeviceSpec.SourceSystem / OpcUaConnectorId；
    // - sourceSequence 形状：WorldHistorySeedService 的 summary 序列
    //   `$"{SequencePrefix}:summary:{DeviceAssetId}:{TagKey}:{bucketStart.ToUnixTimeMilliseconds()}"`。
    private const string Organization = "org-001";
    private const string Environment = "env-dev";
    private const string DeviceAssetId = "DEV-CNC-01";
    private const string TagKey = "parts_count";
    private const string SeedSourceSystem = "seed-world-history";
    private const string SeedSourceConnector = "CONN-OPCUA-01";
    private const string SeedSourceSequence = "seed:world-history:summary:DEV-CNC-01:parts_count:1783843200000";

    // 仓库测试夹具最短的那一组（TelemetryProductionReportAutomationTests.CreateEvent 用的字面量）。
    private const string ShortestFixtureSourceKey =
        "industrialTelemetry:production-count:org-001:env-dev:DEV-PACK-01:parts_count:opcua:opcua-cell-01:seq-002";

    private static string SeedShapedSourceKey => IntegrationEventIdempotencyKey.Compose(
        "industrialTelemetry:production-count:",
        Organization,
        Environment,
        DeviceAssetId,
        TagKey,
        SeedSourceSystem,
        SeedSourceConnector,
        SeedSourceSequence);

    /// <summary>
    /// 复现夹具自身的长度读数：182（本仓命名约定）与 114（仓库最短夹具）。
    /// 这两个数是本票可达性论证的地基，先把它们钉住，避免后续用例悄悄换成别的输入。
    /// </summary>
    [Fact]
    public void Repository_naming_convention_reaches_182_and_shortest_fixture_reaches_114()
    {
        Assert.Equal(182, ("telemetry:" + SeedShapedSourceKey).Length);
        Assert.Equal(114, ("telemetry:" + ShortestFixtureSourceKey).Length);
        Assert.Equal(150, CodeIdempotencyKey.IdempotencyKeyMaxLength);
    }

    /// <summary>
    /// 两道 150 的墙里，命令校验器那一道恰好等于共享编码实体的列宽常量。
    /// 行为探针，不读源码文本：150 过、151 拒。
    /// </summary>
    [Fact]
    public void Command_validator_wall_equals_the_shared_coding_column_width()
    {
        var validator = new RecordProductionReportCommandValidator();
        var atLimit = validator.Validate(RecordCommandWithKey(new string('k', CodeIdempotencyKey.IdempotencyKeyMaxLength)));
        var overLimit = validator.Validate(RecordCommandWithKey(new string('k', CodeIdempotencyKey.IdempotencyKeyMaxLength + 1)));

        Assert.True(atLimit.IsValid, string.Join("; ", atLimit.Errors.Select(x => x.ErrorMessage)));
        Assert.False(overLimit.IsValid);
        Assert.Contains(
            overLimit.Errors,
            x => x.PropertyName == nameof(RecordProductionReportCommand.IdempotencyKey));
    }

    /// <summary>
    /// 调用点 1（确认晋升，TelemetryProductionReportCandidateCommands）：
    /// 本仓命名约定的候选必须能穿过命令幂等键那道 150，并真的落一条报工。
    /// </summary>
    [Fact]
    public async Task Promoting_a_repository_naming_convention_candidate_records_a_report()
    {
        var databaseName = nameof(Promoting_a_repository_naming_convention_candidate_records_a_report);
        await using var dbContext = CreateDbContext(databaseName);
        SeedRunningOperation(dbContext);
        var candidate = TelemetryProductionReportCandidate.CreatePendingConfirmation(
            Organization,
            Environment,
            SeedShapedSourceKey,
            DeviceAssetId,
            TagKey,
            TelemetryProductionReportCandidate.PostedReportingMode,
            3m,
            DateTimeOffset.Parse("2026-07-11T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
            "WC-PACK-01",
            "WO-001",
            "OP-10",
            TelemetryProductionReportCandidate.ActiveAlarmSuspensionReason);
        dbContext.TelemetryProductionReportCandidates.Add(candidate);
        await dbContext.SaveChangesAsync();

        var sender = new ValidatingProductionReportSender(dbContext);
        var result = await new PromoteTelemetryProductionReportCandidateCommandHandler(dbContext, sender).Handle(
            new(Organization, Environment, candidate.Id, "WO-001", "OP-10", "operator:line", DateTimeOffset.Parse("2026-07-11T08:05:00Z")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var report = await dbContext.ProductionReports.SingleAsync();
        Assert.Equal(report.Id, result.Id);
        Assert.Equal(ProductionReport.TelemetrySource, report.Source);
        Assert.Equal(TelemetryProductionReportCandidate.ConfirmedStatus, candidate.Status);

        var observedKey = Assert.Single(sender.ObservedIdempotencyKeys);
        Assert.True(
            observedKey.Length <= CodeIdempotencyKey.IdempotencyKeyMaxLength,
            $"派生键长度 {observedKey.Length} 超过 {CodeIdempotencyKey.IdempotencyKeyMaxLength}：{observedKey}");
    }

    /// <summary>
    /// 调用点 2（直接过账，TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport）：
    /// 这一处在 CAP 消费者里，抛出即毒消息，必须同样穿过那道 150。
    /// </summary>
    [Fact]
    public async Task Direct_posting_of_a_repository_naming_convention_event_records_a_report()
    {
        var databaseName = nameof(Direct_posting_of_a_repository_naming_convention_event_records_a_report);
        await using var dbContext = CreateDbContext(databaseName);
        SeedRunningOperation(dbContext);
        await dbContext.SaveChangesAsync();

        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var sender = new ValidatingProductionReportSender(dbContext);
        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext, deadLetterStore, sender);

        await handler.HandleAsync(CreateSeedShapedEvent(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await using var verification = CreateDbContext(databaseName);
        var report = await verification.ProductionReports.SingleAsync();
        Assert.Equal(ProductionReport.TelemetrySource, report.Source);
        Assert.Equal(3m, report.GoodQuantity);
        Assert.Empty(await deadLetterStore.ListAsync(null, null, CancellationToken.None));

        // 消费侧的第二道保证：inbox 行与报工在同一个 DbContext 上提交，重投被 inbox 挡住而不是再记一条。
        Assert.Single(await verification.ProcessedIntegrationEvents.ToArrayAsync());
        var observedKey = Assert.Single(sender.ObservedIdempotencyKeys);
        Assert.True(
            observedKey.Length <= CodeIdempotencyKey.IdempotencyKeyMaxLength,
            $"派生键长度 {observedKey.Length} 超过 {CodeIdempotencyKey.IdempotencyKeyMaxLength}：{observedKey}");
    }

    /// <summary>
    /// 消费侧重投：inbox 挡住第二次投递，不产生第二条报工，也不抛出。
    /// </summary>
    [Fact]
    public async Task Redelivery_of_the_same_event_does_not_record_a_second_report()
    {
        var databaseName = nameof(Redelivery_of_the_same_event_does_not_record_a_second_report);
        await using var dbContext = CreateDbContext(databaseName);
        SeedRunningOperation(dbContext);
        await dbContext.SaveChangesAsync();

        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext, new InMemoryIntegrationEventDeadLetterStore(), new ValidatingProductionReportSender(dbContext));

        await handler.HandleAsync(CreateSeedShapedEvent(), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await handler.HandleAsync(CreateSeedShapedEvent(), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        await using var verification = CreateDbContext(databaseName);
        Assert.Equal(1, await verification.ProductionReports.CountAsync());
        Assert.Single(await verification.ProcessedIntegrationEvents.ToArrayAsync());
    }

    /// <summary>
    /// **黄金向量**：期望值由仓库之外的两个独立工具算出且互相一致，
    /// 不是先用本实现求值再用本实现复算。
    /// <para>生成方法（两条互为对照，读数逐字相同）：
    /// <code>
    /// printf '%s' "&lt;源键&gt;" | openssl dgst -sha256 -binary | openssl base64 -A | tr '+/' '-_' | tr -d '='
    /// python3 -c "import hashlib,base64,sys; print(base64.urlsafe_b64encode(hashlib.sha256(sys.argv[1].encode()).digest()).decode().rstrip('='))" "&lt;源键&gt;"
    /// </code>
    /// 规范化规则：输入按 UTF-8 编码、SHA-256、base64url（<c>+/</c> 换 <c>-_</c>）、**去掉填充 <c>=</c>**。</para>
    /// <para>它钉住的性质：派生只依赖源信封键这一个输入。任何掺进时钟 / GUID / salt / 进程状态的改法，
    /// 这三格都会红 —— 本仓有过 <c>CanonicalKey</c> 掺 <c>UtcNow</c> 而整格变异全存活的判例，
    /// 那种自指断言在这里不成立。</para>
    /// </summary>
    [Theory]
    [InlineData(
        "industrialTelemetry:production-count:org-001:env-dev:DEV-CNC-01:parts_count:seed-world-history:CONN-OPCUA-01:seed:world-history:summary:DEV-CNC-01:parts_count:1783843200000",
        "telemetry:A3meN9i1JjdRU7e7LosyhWHbFkZPNJH8gzbVC6oM4sw")]
    [InlineData(
        ShortestFixtureSourceKey,
        "telemetry:iGkUaXoh-oon4RQ7fEsC19JFeGDhmZcXLNG0ifeYnO8")]
    [InlineData("", "telemetry:47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU")]
    public void Derivation_matches_externally_computed_golden_vectors(string sourceKey, string expected)
    {
        Assert.Equal(expected, TelemetryProductionReportIdempotencyKey.From(sourceKey));
    }

    /// <summary>
    /// 跨 CAP 重投稳定：同一条来源事实求两次键逐字节相同；不同来源事实不折叠。
    /// </summary>
    [Fact]
    public void Derivation_is_deterministic_and_does_not_fold_distinct_sources()
    {
        var first = TelemetryProductionReportIdempotencyKey.From(SeedShapedSourceKey);
        var again = TelemetryProductionReportIdempotencyKey.From(SeedShapedSourceKey);
        Assert.Equal(first, again);
        Assert.Equal(first, string.Concat(first));

        // 只差最后一个字符的两条来源事实必须落到不同的键（截断式修法会在这里折叠）。
        Assert.NotEqual(first, TelemetryProductionReportIdempotencyKey.From(SeedShapedSourceKey[..^1] + "1"));
        Assert.NotEqual(first, TelemetryProductionReportIdempotencyKey.From(ShortestFixtureSourceKey));
    }

    /// <summary>
    /// 上界与输入长度无关：源端七个身份段全部取到各自列宽上界（合计 900）时仍然定长 53。
    /// </summary>
    /// <remarks>
    /// 列宽读自 <c>telemetry_summaries</c>（source 侧真权威）：
    /// organization_id 100 / environment_id 100 / device_asset_id 150 / tag_key 150 /
    /// source_sequence 150 / source_system 100 / source_connector 150。
    /// 这里的 10000 不是上界，是「远超任何列宽的任意长输入」——本方向要证的正是上界与输入长度无关。
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(10000)]
    public void Derived_key_is_fixed_length_and_fits_the_downstream_budget(int extra)
    {
        var saturated = IntegrationEventIdempotencyKey.Compose(
            "industrialTelemetry:production-count:",
            new string('o', 100),
            new string('e', 100),
            new string('d', 150),
            new string('t', 150),
            new string('y', 100),
            new string('c', 150),
            new string('q', 150));
        foreach (var sourceKey in new[] { saturated, new string('x', extra), SeedShapedSourceKey })
        {
            var derived = TelemetryProductionReportIdempotencyKey.From(sourceKey);
            Assert.Equal(TelemetryProductionReportIdempotencyKey.Length, derived.Length);
            Assert.Equal(53, derived.Length);
            Assert.True(
                derived.Length <= CodeIdempotencyKey.IdempotencyKeyMaxLength,
                $"派生键长度 {derived.Length} 超过 {CodeIdempotencyKey.IdempotencyKeyMaxLength}");
            Assert.StartsWith(TelemetryProductionReportIdempotencyKey.Prefix, derived, StringComparison.Ordinal);
            Assert.True(new RecordProductionReportCommandValidator().Validate(RecordCommandWithKey(derived)).IsValid);
        }
    }

    /// <summary>
    /// **回归哨兵**：改动前的裸拼形态在本仓自己的命名约定下**会**被那道 150 拒，新形态不会。
    /// 这一条保留的是旧实现的反例，删掉有界派生后两个调用点的用例才有对照。
    /// </summary>
    [Fact]
    public void Legacy_concatenated_form_is_rejected_where_the_bounded_form_passes()
    {
        var validator = new RecordProductionReportCommandValidator();

        var legacy = validator.Validate(RecordCommandWithKey("telemetry:" + SeedShapedSourceKey));
        Assert.False(legacy.IsValid);
        var failure = Assert.Single(
            legacy.Errors,
            x => x.PropertyName == nameof(RecordProductionReportCommand.IdempotencyKey));
        Assert.Equal("MaximumLengthValidator", failure.ErrorCode);

        Assert.True(validator.Validate(
            RecordCommandWithKey(TelemetryProductionReportIdempotencyKey.From(SeedShapedSourceKey))).IsValid);
    }

    /// <summary>
    /// 两个调用点对同一条来源事实产出同一把键。
    /// 它们走的是不同的取值路径（候选行的 <c>SourceIdempotencyKey</c> 列 vs 信封上的 <c>IdempotencyKey</c>），
    /// 若哪天有人只改一处，这一条会红。
    /// </summary>
    [Fact]
    public async Task Both_call_sites_derive_the_same_key_for_the_same_source_fact()
    {
        var promoteKey = await CaptureKeyFromPromotionAsync();
        var postingKey = await CaptureKeyFromDirectPostingAsync();
        Assert.Equal(promoteKey, postingKey);
        Assert.Equal(TelemetryProductionReportIdempotencyKey.From(SeedShapedSourceKey), promoteKey);
    }

    private static async Task<string> CaptureKeyFromPromotionAsync()
    {
        await using var dbContext = CreateDbContext("both-call-sites-promotion-" + Guid.NewGuid().ToString("N"));
        SeedRunningOperation(dbContext);
        var candidate = TelemetryProductionReportCandidate.CreatePendingConfirmation(
            Organization, Environment, SeedShapedSourceKey, DeviceAssetId, TagKey,
            TelemetryProductionReportCandidate.PostedReportingMode, 3m,
            DateTimeOffset.Parse("2026-07-11T08:00:00Z"), DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
            "WC-PACK-01", "WO-001", "OP-10", TelemetryProductionReportCandidate.ActiveAlarmSuspensionReason);
        dbContext.TelemetryProductionReportCandidates.Add(candidate);
        await dbContext.SaveChangesAsync();

        var sender = new ValidatingProductionReportSender(dbContext);
        await new PromoteTelemetryProductionReportCandidateCommandHandler(dbContext, sender).Handle(
            new(Organization, Environment, candidate.Id, "WO-001", "OP-10", "operator:line", DateTimeOffset.Parse("2026-07-11T08:05:00Z")),
            CancellationToken.None);
        return Assert.Single(sender.ObservedIdempotencyKeys);
    }

    private static async Task<string> CaptureKeyFromDirectPostingAsync()
    {
        await using var dbContext = CreateDbContext("both-call-sites-posting-" + Guid.NewGuid().ToString("N"));
        SeedRunningOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var sender = new ValidatingProductionReportSender(dbContext);
        await new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext, new InMemoryIntegrationEventDeadLetterStore(), sender)
            .HandleAsync(CreateSeedShapedEvent(), CancellationToken.None);
        return Assert.Single(sender.ObservedIdempotencyKeys);
    }

    private static RecordProductionReportCommand RecordCommandWithKey(string idempotencyKey) => new(
        Organization, Environment, "WO-001", "OP-10", 3m, 0m, false,
        DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
        idempotencyKey,
        Source: ProductionReport.TelemetrySource);

    private static TelemetryProductionCountDeltaIntegrationEvent CreateSeedShapedEvent() => new(
        "evt-production-count-3477",
        IndustrialTelemetryIntegrationEventTypes.ProductionCountDeltaRecorded,
        IndustrialTelemetryIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
        IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
        $"industrialTelemetry:production-count:{Organization}:{Environment}:{DeviceAssetId}:{TagKey}:{SeedSourceSequence}",
        "summary-3477",
        Organization,
        Environment,
        "system:industrial-telemetry",
        SeedShapedSourceKey,
        new TelemetryProductionCountDeltaPayload(
            DeviceAssetId,
            TagKey,
            TelemetryProductionReportCandidate.PostedReportingMode,
            3m,
            DateTimeOffset.Parse("2026-07-11T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
            SeedSourceSequence,
            HasActiveAlarm: false));

    private static void SeedRunningOperation(ApplicationDbContext dbContext)
    {
        var workOrder = WorkOrder.Create(Organization, Environment, "WO-001", "SKU-FG", "PV-001", 10m, 1, DateTimeOffset.Parse("2026-07-11T07:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-07-11T07:00:00Z"));
        var operation = OperationTask.Create(
            Organization, Environment, "WO-001", "OP-10", OperationTaskLifecycleStatus.InProgress, 10, "WC-PACK-01", [],
            DateTimeOffset.Parse("2026-07-11T07:00:00Z"), TimeSpan.FromHours(1), DateTimeOffset.Parse("2026-07-11T07:00:00Z"), null, "SKU-001");
        operation.Assign(null, DeviceAssetId, null, DateTimeOffset.Parse("2026-07-11T07:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(operation);
        dbContext.DeviceAssetWorkCenterMappings.Add(DeviceAssetWorkCenterMapping.Create(Organization, Environment, DeviceAssetId, "WC-PACK-01"));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName).Options, new NoopMediator());

    /// <summary>
    /// 把生产管线里那一步校验放回调用链：用的是 <c>Program.cs:.AddKnownExceptionValidationBehavior()</c>
    /// 注册的**同一个类型** <see cref="KnownExceptionValidationBehavior{TRequest,TResponse}"/>
    /// 加**同一个校验器** <see cref="RecordProductionReportCommandValidator"/>，不是手搓的等价物。
    /// <para><b>它不证明什么</b>：这里只装配了校验这一段行为，没有装配完整的 MediatR 管线、
    /// UoW/事务行为、真 HTTP 边界与真 PostgreSQL；这些由各自的 lane 承担。</para>
    /// </summary>
    private sealed class ValidatingProductionReportSender(ApplicationDbContext dbContext) : ISender
    {
        private readonly KnownExceptionValidationBehavior<RecordProductionReportCommand, ProductionReportCommandResult> _validation =
            new([new RecordProductionReportCommandValidator()]);

        public MesCodingService CodingService { get; } = new();

        public List<string> ObservedIdempotencyKeys { get; } = [];

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is RecordProductionReportCommand command)
            {
                ObservedIdempotencyKeys.Add(command.IdempotencyKey);
                var response = await _validation.Handle(
                    command,
                    async (behaviorCancellationToken) =>
                    {
                        var handled = await new RecordProductionReportCommandHandler(
                            dbContext,
                            TestProductionReportOeeDimensionSnapshotProvider.Instance,
                            TestMesFirstArticleGate.Allowing,
                            CodingService).Handle(command, behaviorCancellationToken);
                        await dbContext.SaveChangesAsync(behaviorCancellationToken);
                        return handled;
                    },
                    cancellationToken);
                return (TResponse)(object)response;
            }

            throw new NotSupportedException($"Unsupported request: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
