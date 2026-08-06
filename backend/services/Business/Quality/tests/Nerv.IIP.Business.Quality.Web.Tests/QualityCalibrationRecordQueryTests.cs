using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Queries.MeasuringDevices;
using Nerv.IIP.Testing;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 校准记录读面回归测试。
///
/// 背景：该读面在真机上**必然** 500（网关回 <c>downstream-request-failed</c>）。根因是查询先把 join
/// 结果投影成 positional record，再在投影之上拼 Where/OrderBy —— positional record 的构造是
/// <c>Members == null</c> 的 NewExpression，EF Core 无法把 <c>new Projection(...).Prop</c> 归约回列访问，
/// 于是翻译失败抛 <see cref="InvalidOperationException"/>。因为排序是**无条件**的，连不带任何筛选参数的
/// 请求也会炸。
///
/// 当初逃过 CI 的原因：服务侧该读面零测试，而网关测试用的是 fake 实现 —— 这条 LINQ 从未对任何
/// 关系型 provider 执行过。所以这里的第一组测试刻意做成**不需要数据库**：EF 的查询翻译发生在建立连接
/// **之前**，因此指向一个预期被拒的 Npgsql 连接串就能把「翻译失败」和「连不上库」区分开，从而在 CI 里
/// 真正门禁住这类 provider 翻译回归。
///
/// 注意不要改用 SQLite 兜这个底：SQLite 不支持 <c>DateTimeOffset</c> 的 ORDER BY，会给出与生产
/// provider 无关的假红；而 InMemory provider 会做客户端求值，给出假绿。
///
/// 「连不上库」这一步不能靠某个 IP 恰好超时：连接目标由
/// <see cref="NetworkFailureFixture.ReserveRefusedLoopbackEndpoint"/> 提供 —— 一个刚绑定又立刻释放的
/// 本地端口，不经 DNS 解析器也不经防火墙策略，因此结果不随机器配置而变。这**不是**绝对保证：该端口
/// 在返回后回到 ephemeral 池，仍存在被同机其他进程抢占的窗口（详见该方法的 XML doc 与 #1477）。
/// 下面的分类断言正是这一前提的守卫 —— 前提一旦被破坏，是断言失败而不是静默改变语义。
/// </summary>
public sealed class QualityCalibrationRecordQueryTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";

    // 两档预算显式分开，不用一个模糊总时长冒充两者。connect 预算按「本地 loopback 立即 RST」取小：
    // 这条用例期望的正是连接被拒，2s 只是停滞上限而非预期等待。request 预算取秒级并**大于** connect
    // 预算：它约束的是连接建立之后的单条命令，对着被拒端点不可能被触发，但翻译探针一旦被改成对着真库
    // 跑，命令就该按真实依赖的正常抖动兜底。
    private static readonly TimeSpan ConnectBudget = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 指向一个预期被拒的本地端口：翻译成功后才会走到连接失败。
    ///
    /// 刻意是**实例**字段而不是 <c>static</c>：xUnit 为每个测试新建一次实例，因此端口按用例预留，
    /// 而不是由整个测试类长期持有 —— 后者会把「端口已释放、可被同机其他进程占用」的暴露窗口拉长到
    /// 整个类的生命周期。
    /// </summary>
    private readonly RefusedTcpEndpoint _refusedEndpoint =
        NetworkFailureFixture.ReserveRefusedLoopbackEndpoint();

    /// <summary>
    /// 五日期 Theory（含 2026-07-27 边界日）× 全部筛选组合：断言查询能被生产 provider 翻译。
    /// 只要 LINQ 不可翻译，EF 会在建连之前抛 <see cref="InvalidOperationException"/>；
    /// 可翻译则一定走到连接失败 —— 用这个差别把回归钉死，且不依赖任何数据库。
    /// </summary>
    [Theory]
    [InlineData("2026-01-15")]
    [InlineData("2026-03-31")]
    [InlineData("2026-06-30")]
    [InlineData("2026-07-27")]
    [InlineData("2026-12-31")]
    public async Task Calibration_record_query_is_translatable_by_the_production_provider(string asOfDate)
    {
        var anchor = new DateTimeOffset(DateOnly.Parse(asOfDate), TimeOnly.MinValue, TimeSpan.Zero);

        foreach (var query in EveryFilterCombination(anchor))
        {
            await using var dbContext = CreateNpgsqlContext();
            var handler = new ListCalibrationRecordsQueryHandler(dbContext);

            var exception = await Record.ExceptionAsync(() => handler.Handle(query, CancellationToken.None));

            Assert.NotNull(exception);
            AssertFailedOnConnectionNotTranslation(exception!);
        }
    }

    /// <summary>真实 PostgreSQL 下的行为断言。默认 skip；设 <c>NERV_IIP_TEST_POSTGRES</c> 后运行。</summary>
    [QualityPostgresFact]
    public async Task Calibration_records_are_filtered_ordered_and_scoped_on_postgres()
    {
        var anchor = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
        await using var database = await QualityPostgresTestDatabase.CreateAsync(
            nameof(Calibration_records_are_filtered_ordered_and_scoped_on_postgres));
        var connectionString = database.ConnectionString;

        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddQualityPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        // 用一次性编码前缀把本用例的数据与库里既有种子隔开。
        var tag = $"CALTEST-{Guid.CreateVersion7():N}"[..24];
        var device = MeasuringDevice.Create(
            OrganizationId, EnvironmentId, $"MD-{tag}", "caliper", "0.01mm", 90, anchor.AddDays(-20));
        device.RecordCalibration($"{tag}-001", anchor.AddDays(-20), "宁沪计量检定中心", "file-cal-001");
        device.RecordCalibration($"{tag}-002", anchor.AddDays(-10), "宁沪计量检定中心", null);
        device.RecordCalibration($"{tag}-003", anchor.AddDays(-1), "省计量科学研究院", "file-cal-003");
        dbContext.MeasuringDevices.Add(device);

        var foreign = MeasuringDevice.Create(
            "org-999", "env-other", $"MD-{tag}-X", "caliper", "0.01mm", 90, anchor);
        foreign.RecordCalibration($"{tag}-FOREIGN", anchor, "外部计量所", null);
        dbContext.MeasuringDevices.Add(foreign);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ListCalibrationRecordsQueryHandler(dbContext);

        // 器具维度筛选（强类型 id）：只回本器具的三条。
        var byDevice = await handler.Handle(
            new ListCalibrationRecordsQuery(OrganizationId, EnvironmentId, MeasuringDeviceId: device.Id),
            CancellationToken.None);
        Assert.Equal(3, byDevice.Total);
        Assert.All(byDevice.Items, x => Assert.Equal(device.Id, x.MeasuringDeviceId));

        // 排序为「校准时刻倒序」。
        Assert.Equal(
            new[] { $"{tag}-003", $"{tag}-002", $"{tag}-001" },
            byDevice.Items.Select(x => x.CalibrationNo).ToArray());

        // 下次到期 = 校准时刻 + 器具周期（90 天），且回带了器具编码/类型。
        var latest = byDevice.Items.First();
        Assert.Equal(anchor.AddDays(-1).AddDays(90), latest.NextCalibrationDueAtUtc);
        Assert.Equal($"MD-{tag}", latest.DeviceCode);
        Assert.Equal("caliper", latest.DeviceType);

        // 时间窗筛选：只有 anchor-10d 与 anchor-1d 两条落在窗内。
        var windowed = await handler.Handle(
            new ListCalibrationRecordsQuery(
                OrganizationId,
                EnvironmentId,
                MeasuringDeviceId: device.Id,
                CalibratedFromUtc: anchor.AddDays(-15),
                CalibratedToUtc: anchor),
            CancellationToken.None);
        Assert.Equal(2, windowed.Total);

        // 关键字筛选走 lower() LIKE。
        var byKeyword = await handler.Handle(
            new ListCalibrationRecordsQuery(OrganizationId, EnvironmentId, Keyword: $"{tag}-002".ToLowerInvariant()),
            CancellationToken.None);
        Assert.Equal(1, byKeyword.Total);
        Assert.Equal($"{tag}-002", byKeyword.Items.Single().CalibrationNo);

        // 分页作用在实体查询上，Total 仍是筛选后的总数。
        var paged = await handler.Handle(
            new ListCalibrationRecordsQuery(OrganizationId, EnvironmentId, MeasuringDeviceId: device.Id, Skip: 1, Take: 1),
            CancellationToken.None);
        Assert.Equal(3, paged.Total);
        Assert.Equal($"{tag}-002", Assert.Single(paged.Items).CalibrationNo);

        // 组织/环境隔离：另一租户的器具不得泄漏。
        var scoped = await handler.Handle(
            new ListCalibrationRecordsQuery(OrganizationId, EnvironmentId, Keyword: tag.ToLowerInvariant()),
            CancellationToken.None);
        Assert.DoesNotContain(scoped.Items, x => x.CalibrationNo.EndsWith("FOREIGN", StringComparison.Ordinal));

        // 清理本用例写入的数据，保持 Postgres profile 可重复运行。
        dbContext.MeasuringDevices.RemoveRange(device, foreign);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>覆盖「无筛选」到「全筛选」的每种组合 —— 无条件排序意味着裸查询也必须能翻译。</summary>
    private static IEnumerable<ListCalibrationRecordsQuery> EveryFilterCombination(DateTimeOffset anchor)
    {
        yield return new ListCalibrationRecordsQuery(OrganizationId, EnvironmentId);
        yield return new ListCalibrationRecordsQuery(
            OrganizationId, EnvironmentId, MeasuringDeviceId: new MeasuringDeviceId(Guid.CreateVersion7()));
        yield return new ListCalibrationRecordsQuery(
            OrganizationId, EnvironmentId, CalibratedFromUtc: anchor.AddDays(-15));
        yield return new ListCalibrationRecordsQuery(
            OrganizationId, EnvironmentId, CalibratedToUtc: anchor);
        yield return new ListCalibrationRecordsQuery(
            OrganizationId, EnvironmentId, Keyword: "cal-002");
        yield return new ListCalibrationRecordsQuery(
            OrganizationId,
            EnvironmentId,
            MeasuringDeviceId: new MeasuringDeviceId(Guid.CreateVersion7()),
            Keyword: "cal-002",
            CalibratedFromUtc: anchor.AddDays(-15),
            CalibratedToUtc: anchor,
            Skip: 1,
            Take: 50);
    }

    private static IEnumerable<Exception> Unwind(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static void AssertFailedOnConnectionNotTranslation(Exception exception)
    {
        foreach (var current in Unwind(exception))
        {
            Assert.DoesNotContain("could not be translated", current.Message, StringComparison.OrdinalIgnoreCase);
        }

        // 翻译成功的证据：执行走到了「建立连接」这一步才失败。若 LINQ 不可翻译，EF 会在建连之前
        // 就抛出 InvalidOperationException，异常链里不会出现任何 socket 层异常。
        var reachedTransport = Unwind(exception).Any(x => x is System.Net.Sockets.SocketException);

        Assert.True(
            reachedTransport,
            "查询未能被生产 provider 翻译（执行在建立连接之前就失败了）：\n"
                + string.Join("\n", Unwind(exception).Select(x => $"  {x.GetType().FullName}: {x.Message}")));

        // 且失败必须正好是「连接被拒」：既不是 DNS，也不是某个恰好超时的地址。
        var observation = NetworkFailureClassifier.FromException(exception, CancellationToken.None);
        Assert.Equal(NetworkFailureKind.ConnectionRefused, observation.Kind);
    }

    private ApplicationDbContext CreateNpgsqlContext()
    {
        var connectionString = RefusedPostgres.ConnectionString(
            _refusedEndpoint,
            database: "nerv_iip_translation_probe",
            username: "probe",
            password: "probe",
            connectBudget: ConnectBudget,
            requestBudget: RequestBudget);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "quality"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

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
}
