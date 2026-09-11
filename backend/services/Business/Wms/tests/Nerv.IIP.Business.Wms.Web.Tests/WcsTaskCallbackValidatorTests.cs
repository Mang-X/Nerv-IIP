using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using FluentValidation.Validators;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Validation;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.DistributedLocking;
using Nerv.IIP.Testing.PostgreSql;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;
using Npgsql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// WCS 回调两条命令的入参校验契约（#3305）。
/// </summary>
/// <remarks>
/// <para><b>改前事实</b>：<c>CompleteWcsTaskCommand</c> 与 <c>FailWcsTaskCommand</c> 两条命令、
/// 以及它们的两个请求 DTO，全仓 <c>Validator</c> 命中 0；
/// 端点把 <c>req</c> 逐字段直接塞进命令。输入来自外部 WCS，不在本仓控制内。</para>
///
/// <para><b>本类钉住三件事</b>：</para>
/// <list type="number">
/// <item>校验器声明的上界**逐条等于承载列宽**（<see cref="Declared_bounds_equal_the_carrying_column_widths"/>）——
/// 数字只有 <see cref="WcsTaskCallbackFieldPolicy"/> 一份且被 EF 模型钉住，任一侧单边改动即红；</item>
/// <item><c>failure_message</c> **确实是无界列**（<see cref="Failure_message_column_is_unbounded"/>）——
/// 谁把 <c>HasMaxLength</c> 加回去就红，这是本票主结论的落点；</item>
/// <item>逐字段的**真实可达性**：哪些规则在真实 MediatR 管道里拦得住、哪些被外层
/// <c>NervIipCommandLockBehavior</c> 抢答（<see cref="Callback_pipeline_reachability_is_exactly_as_documented"/>）。</item>
/// </list>
///
/// <para><b>合同分类</b>（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是 <c>WmsEntityTypeConfigurations</c> 的列宽声明与 FluentValidation 规则树；
/// <c>Regression</c> 的权威来源是 GitHub #3305。</para>
///
/// <para><b>值域边界（声明放弃了什么，别读成完备）</b>：</para>
/// <list type="bullet">
/// <item>列宽读的是 <b>EF 模型</b>而不是迁移脚本。模型与迁移单边漂移不由本类抓——
/// 它由 EF 自己在 <c>MigrateAsync</c> 处抛 pending-model-changes 暴露，因而只在跑真库迁移的用例上显形
/// （本 PR 的 M3 变异格实测：把 <c>HasMaxLength(1000)</c> 加回去后 10 条 Postgres 用例因此转红）。
/// <b>本仓没有独立的 pending-model-changes 门禁脚本</b>——<c>HasPendingModelChanges</c> 全仓零调用点，
/// 别照抄别处注释里那句「由 pending-model-changes 门禁承担」。</item>
/// <item>本类不证明真库落库行为；那一面由本 PR 正文里 <c>postgres:18</c> 的
/// <c>information_schema</c> 读数承担。</item>
/// <item>本类不覆盖 Notification 侧摘要渲染；那一面由
/// <c>NotificationSummaryTextTests</c> 与跨服务契约用例
/// <c>WcsFailureMessageCrossServiceSummaryContractTests</c> 承担。</item>
/// </list>
/// </remarks>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WcsTaskCallbackValidatorTests
{
    private const string PostgresConnectionStringEnvironmentVariable = "NERV_IIP_TEST_POSTGRES";
    private const string ValidOrganizationId = "org-001";
    private const string ValidEnvironmentId = "env-dev";
    private const string ValidExternalTaskId = "EXT-3305-001";

    /// <summary>
    /// 校验器上界 ↔ 承载列宽的逐条对撞。
    /// </summary>
    /// <remarks>
    /// 左边是 <see cref="WcsTaskCallbackFieldPolicy"/> 的常量，右边是 <c>wcs_tasks</c> 对应列的
    /// <c>GetMaxLength()</c>。<b>两侧都不是从对方抄来的</b>：常量给校验器用，列宽由
    /// EntityConfiguration 与迁移决定。任一单边改动这条即红。
    /// 同时把「校验器真的按这些常量声明了上界」也读出来——否则常量对了、规则里写了别的数照绿。
    /// </remarks>
    [Fact]
    public async Task Declared_bounds_equal_the_carrying_column_widths()
    {
        using var fixture = CreateModelFixture();
        var wcsTask = fixture.Model.FindEntityType(typeof(WcsTask))
            ?? throw new InvalidOperationException("WMS 模型里找不到 WcsTask 实体。");

        Assert.Equal(WcsTaskCallbackFieldPolicy.TenantIdMaxLength, MaxLength(wcsTask, "OrganizationId"));
        Assert.Equal(WcsTaskCallbackFieldPolicy.TenantIdMaxLength, MaxLength(wcsTask, "EnvironmentId"));
        Assert.Equal(WcsTaskCallbackFieldPolicy.ExternalTaskIdMaxLength, MaxLength(wcsTask, nameof(WcsTask.ExternalTaskId)));
        Assert.Equal(WcsTaskCallbackFieldPolicy.FailureCodeMaxLength, MaxLength(wcsTask, nameof(WcsTask.FailureCode)));

        // 规则树里声明的上界集合必须恰好是这四条（Complete 侧三条、Fail 侧四条），
        // 少一条 / 多一条 / 改一个数都红。
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();

        Assert.Equal(
            new[]
            {
                $"EnvironmentId<={WcsTaskCallbackFieldPolicy.TenantIdMaxLength}",
                $"ExternalTaskId<={WcsTaskCallbackFieldPolicy.ExternalTaskIdMaxLength}",
                $"OrganizationId<={WcsTaskCallbackFieldPolicy.TenantIdMaxLength}",
            },
            DeclaredLengthBounds<CompleteWcsTaskCommand>(scope.ServiceProvider));
        Assert.Equal(
            new[]
            {
                $"EnvironmentId<={WcsTaskCallbackFieldPolicy.TenantIdMaxLength}",
                $"ExternalTaskId<={WcsTaskCallbackFieldPolicy.ExternalTaskIdMaxLength}",
                $"FailureCode<={WcsTaskCallbackFieldPolicy.FailureCodeMaxLength}",
                $"OrganizationId<={WcsTaskCallbackFieldPolicy.TenantIdMaxLength}",
            },
            DeclaredLengthBounds<FailWcsTaskCommand>(scope.ServiceProvider));
    }

    /// <summary>
    /// <c>failure_message</c> 与 <c>completion_payload_json</c> 都必须是无界列。
    /// </summary>
    /// <remarks>
    /// 这是本票主结论的落点：外部 WCS 回传的原始诊断报文不该有人为上界，
    /// 拒绝一次回调的代价是任务卡在「执行中」、要现场人工介入。
    /// <c>completion_payload_json</c> 一并读出来，是为了让「隔壁先例」这句话有断言、不只写在注释里；
    /// 它同时充当**反同义反复对照**：这条用例不是把「所有列都无界」读成绿，
    /// 上面那条对撞里的四列各自有界。
    /// </remarks>
    [Fact]
    public void Failure_message_column_is_unbounded()
    {
        using var fixture = CreateModelFixture();
        var wcsTask = fixture.Model.FindEntityType(typeof(WcsTask))
            ?? throw new InvalidOperationException("WMS 模型里找不到 WcsTask 实体。");

        Assert.Null(Property(wcsTask, nameof(WcsTask.FailureMessage)).GetMaxLength());
        Assert.Null(Property(wcsTask, nameof(WcsTask.CompletionPayloadJson)).GetMaxLength());
        Assert.Equal("failure_message", ColumnName(wcsTask, nameof(WcsTask.FailureMessage)));
    }

    /// <summary>
    /// 直接调用校验器的逐格读数：违例格、恰好取到上界格、以及**超长诊断报文必须放行**。
    /// </summary>
    /// <remarks>
    /// 「恰好上界」那几格不可省：只有违例格时，把 <c>MaximumLength(100)</c> 收成 <c>(1)</c> 照绿——
    /// 违例格只证明「超界会红」，证不到「界在哪」。
    /// 最后那格是本票的方向格：诊断报文放行任意长度，谁把上界加回去它就红。
    /// </remarks>
    [Fact]
    public async Task Validator_rules_discriminate_on_each_field()
    {
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();
        var complete = scope.ServiceProvider.GetRequiredService<IValidator<CompleteWcsTaskCommand>>();
        var fail = scope.ServiceProvider.GetRequiredService<IValidator<FailWcsTaskCommand>>();

        Assert.True(Validate(complete, CompleteCommand()).IsValid);
        Assert.True(Validate(fail, FailCommand()).IsValid);

        AssertRejectedOn(fail, FailCommand(failureCode: ""), nameof(FailWcsTaskCommand.FailureCode));
        AssertRejectedOn(
            fail,
            FailCommand(failureCode: new string('c', WcsTaskCallbackFieldPolicy.FailureCodeMaxLength + 1)),
            nameof(FailWcsTaskCommand.FailureCode));
        AssertRejectedOn(fail, FailCommand(failureMessage: ""), nameof(FailWcsTaskCommand.FailureMessage));
        AssertRejectedOn(
            fail,
            FailCommand(externalTaskId: new string('x', WcsTaskCallbackFieldPolicy.ExternalTaskIdMaxLength + 1)),
            nameof(FailWcsTaskCommand.ExternalTaskId));
        AssertRejectedOn(complete, CompleteCommand(completionPayloadJson: ""), nameof(CompleteWcsTaskCommand.CompletionPayloadJson));
        AssertRejectedOn(
            complete,
            CompleteCommand(organizationId: new string('o', WcsTaskCallbackFieldPolicy.TenantIdMaxLength + 1)),
            nameof(CompleteWcsTaskCommand.OrganizationId));

        // 恰好取到上界：合法。
        Assert.True(Validate(fail, FailCommand(
            failureCode: new string('c', WcsTaskCallbackFieldPolicy.FailureCodeMaxLength))).IsValid);
        Assert.True(Validate(fail, FailCommand(
            externalTaskId: new string('x', WcsTaskCallbackFieldPolicy.ExternalTaskIdMaxLength))).IsValid);
        Assert.True(Validate(complete, CompleteCommand(
            organizationId: new string('o', WcsTaskCallbackFieldPolicy.TenantIdMaxLength))).IsValid);

        // 方向格：诊断报文没有上界，远超改前的 1000 也必须放行。
        Assert.True(Validate(fail, FailCommand(failureMessage: new string('m', 100_000))).IsValid);
    }

    /// <summary>
    /// 真实 host 里的注册与管道站位读数——<b>写了规则 ≠ 拦得住</b>（#3291 的教训）。
    /// </summary>
    /// <remarks>
    /// <para>两条命令各自能从**真实 host 容器**解析出恰好一个校验器：#3291 的缺陷正是
    /// 「规则写得整整齐齐、容器里解析数为 0」，那种形状编译期完全无声，
    /// 只有从真实容器解析才看得见。</para>
    /// <para>同时把这两条命令的**行为链顺序**读出来钉住：
    /// <c>NervIipCommandLockBehavior</c> 位于校验行为**之前**，
    /// 而这两条命令都注册了 <c>WcsTaskCallbackCommandLock</c>——锁提供者会先按
    /// (org, env, externalTaskId) 查一次 <c>wcs_tasks</c>，查不到就抛
    /// <c>KnownException("未找到 WCS 任务…")</c>。由此得到的逐字段结论并不一致，
    /// 必须分开讲，不许合并成一句「补了校验器」：</para>
    /// <list type="bullet">
    /// <item><b><c>FailureCode</c> 超长</b>：任务存在 ⇒ 锁放行 ⇒ 校验器拒。
    /// <b>这是本票新增的活防线</b>——改前这一格会走到 <c>SaveChangesAsync</c> 抛 22001
    /// （<c>WcsTask.Fail</c> 那一行不在任何 <c>catch</c> 里，外部 WCS 收到 500 后会无限重投）。
    /// 端到端读数见 <see cref="Real_postgres_rejects_an_over_long_failure_code_and_stores_an_unbounded_message"/>。</item>
    /// <item><b><c>ExternalTaskId</c> / 租户两段（<c>OrganizationId</c> / <c>EnvironmentId</c>）</b>：
    /// 这三个字段上的**两类规则都跑不到，不只是 <c>MaximumLength</c></b>——
    /// 超长取值不可能匹配到已存在的行，空取值同样匹配不到，两种情况锁都先抛「未找到 WCS 任务」。
    /// 它们也不构成 22001 防线：这三个字段在两条 handler 里只出现在 WHERE 谓词，一次都不落库。
    /// 留着是命令自身的声明契约，<b>不得当成活防线宣传</b>。</item>
    /// </list>
    /// </remarks>
    [Fact]
    public async Task Callback_commands_resolve_validators_and_sit_behind_the_command_lock()
    {
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IValidator<CompleteWcsTaskCommand>>());
        Assert.Single(scope.ServiceProvider.GetServices<IValidator<FailWcsTaskCommand>>());

        // 两条命令都真的挂了 WcsTaskCallbackCommandLock——没有它，下面那句「锁先抢答」不成立。
        Assert.Single(
            scope.ServiceProvider.GetServices<ICommandLock<CompleteWcsTaskCommand>>()
                .OfType<WcsTaskCallbackCommandLock<CompleteWcsTaskCommand>>());
        Assert.Single(
            scope.ServiceProvider.GetServices<ICommandLock<FailWcsTaskCommand>>()
                .OfType<WcsTaskCallbackCommandLock<FailWcsTaskCommand>>());

        // 行为链顺序：容器按注册顺序返回，第一个即最外层。
        AssertLockRunsBeforeValidation<CompleteWcsTaskCommand>(scope.ServiceProvider);
        AssertLockRunsBeforeValidation<FailWcsTaskCommand>(scope.ServiceProvider);
    }

    /// <summary>
    /// 真库端到端：超长码值被拒在 handler 之前，而任意长的诊断报文原样落库。
    /// </summary>
    /// <remarks>
    /// <para>这一格是本票**唯一**能同时证明两个方向的位置，只有真实 Postgres 才有
    /// <c>varchar(n)</c> 的 22001 与 <c>text</c> 的无界：InMemory provider 对两者都无感，
    /// 在那上面跑这条会双向假绿。</para>
    /// <para>CI 默认跳过（未设 <c>NERV_IIP_TEST_POSTGRES</c>）；
    /// 本机 <c>postgres:18</c> 的实跑读数写在 PR 正文。</para>
    /// </remarks>
    [WmsWcsCallbackBoundPostgresFact]
    public async Task Real_postgres_rejects_an_over_long_failure_code_and_stores_an_unbounded_message()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_wms_wcs_callback_bound");

        await using (var setup = CreateNpgsqlContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
        }

        // 物理列类型读回：迁移真的把 failure_message 改成了 text，而 failure_code 仍是 varchar(100)。
        Assert.Equal(("text", (int?)null), await ColumnTypeAsync(database.ConnectionString, "failure_message"));
        Assert.Equal(
            ("character varying", (int?)WcsTaskCallbackFieldPolicy.FailureCodeMaxLength),
            await ColumnTypeAsync(database.ConnectionString, "failure_code"));

        await using var context = CreateNpgsqlContext(database.ConnectionString);
        var task = WcsTask.Dispatch(
            ValidOrganizationId,
            ValidEnvironmentId,
            new Domain.AggregatesModel.WarehouseTaskAggregate.WarehouseTaskId(Guid.CreateVersion7()),
            "agv",
            ValidExternalTaskId,
            """{"op":"move"}""");
        context.WcsTasks.Add(task);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // 改前形状：越界码值送进 varchar(100) 就是 22001。这一格证明缺陷不是假想的。
        var stored = await context.WcsTasks.SingleAsync();
        stored.Fail(new string('c', WcsTaskCallbackFieldPolicy.FailureCodeMaxLength + 1), "over long code");
        var overflow = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("22001", FindPostgresSqlState(overflow));
        context.ChangeTracker.Clear();

        // 校验器把这一格拦在 handler 之前——同一个越界取值，改后走命令层直接被拒。
        await using var validationHost = CreateHost();
        using var validationScope = validationHost.Services.CreateScope();
        var validator = validationScope.ServiceProvider.GetRequiredService<IValidator<FailWcsTaskCommand>>();
        Assert.False(validator.Validate(FailCommand(
            failureCode: new string('c', WcsTaskCallbackFieldPolicy.FailureCodeMaxLength + 1))).IsValid);

        // 另一个方向：改前 1000 上界之外的诊断报文现在原样落库、原样读回。
        var raw = new string('m', 5_000);
        var reloaded = await context.WcsTasks.SingleAsync();
        reloaded.Fail("PLC_TIMEOUT", raw);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(raw, (await context.WcsTasks.AsNoTracking().SingleAsync()).FailureMessage);
    }

    private static async Task<(string DataType, int? MaxLength)> ColumnTypeAsync(string connectionString, string column)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data_type, character_maximum_length FROM information_schema.columns "
            + "WHERE table_schema = 'wms' AND table_name = 'wcs_tasks' AND column_name = @column;";
        command.Parameters.AddWithValue("column", column);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"wms.wcs_tasks 上没有 {column} 列。");
        return (reader.GetString(0), await reader.IsDBNullAsync(1) ? null : reader.GetInt32(1));
    }

    private static string? FindPostgresSqlState(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException.SqlState;
            }
        }

        return null;
    }

    private static ApplicationDbContext CreateNpgsqlContext(string connectionString) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", WmsFacts.Schema))
                .Options,
            new WcsTaskCallbackNoopMediator());

    /// <summary>
    /// 从真实 host 容器读这两条命令的行为链，断言锁行为排在校验行为之前。
    /// </summary>
    /// <remarks>
    /// 校验行为的类型来自 netcorepal（<c>AddKnownExceptionValidationBehavior</c> 注册），
    /// 这里按「实现了 <see cref="IPipelineBehavior{TRequest,TResponse}"/> 且类型名里含 Validation」定位而不写死全名，
    /// 但要求恰好命中一个：命中 0 个或多个都说明这条读法失配，直接红而不是静默跳过。
    /// </remarks>
    private static void AssertLockRunsBeforeValidation<TCommand>(IServiceProvider services)
        where TCommand : IBaseCommand
    {
        var behaviors = services.GetServices<IPipelineBehavior<TCommand, Unit>>().ToArray();
        var lockIndex = Array.FindIndex(behaviors, x => x is NervIipCommandLockBehavior<TCommand, Unit>);
        var validationIndexes = behaviors
            .Select((behavior, index) => (behavior, index))
            .Where(x => x.behavior.GetType().Name.Contains("Validation", StringComparison.Ordinal))
            .Select(x => x.index)
            .ToArray();

        Assert.True(lockIndex >= 0, $"{typeof(TCommand).Name} 的行为链里没有 NervIipCommandLockBehavior。");
        var validationIndex = Assert.Single(validationIndexes);
        Assert.True(
            lockIndex < validationIndex,
            $"{typeof(TCommand).Name} 的行为链顺序变了：锁行为在 {lockIndex}、校验行为在 {validationIndex}。"
            + "本类与 WcsTaskCallbackValidation 的注释都按「锁在校验之前」描述逐字段可达性，"
            + "顺序真变了要同步改写那些说明，而不是把这条断言删掉。");
    }

    private static CompleteWcsTaskCommand CompleteCommand(
        string? organizationId = null,
        string? environmentId = null,
        string? externalTaskId = null,
        string? completionPayloadJson = null) =>
        new(
            organizationId ?? ValidOrganizationId,
            environmentId ?? ValidEnvironmentId,
            externalTaskId ?? ValidExternalTaskId,
            completionPayloadJson ?? """{"actualQuantity":1}""");

    private static FailWcsTaskCommand FailCommand(
        string? organizationId = null,
        string? environmentId = null,
        string? externalTaskId = null,
        string? failureCode = null,
        string? failureMessage = null) =>
        new(
            organizationId ?? ValidOrganizationId,
            environmentId ?? ValidEnvironmentId,
            externalTaskId ?? ValidExternalTaskId,
            failureCode ?? "PLC_TIMEOUT",
            failureMessage ?? "blocked aisle");

    private static void AssertRejectedOn(IValidator validator, object command, string expectedProperty)
    {
        var result = Validate(validator, command);
        Assert.False(result.IsValid, $"期望 {expectedProperty} 被拒，实际放行。");
        var properties = result.Errors.Select(error => error.PropertyName).Distinct().ToArray();
        Assert.Contains(expectedProperty, properties, StringComparer.OrdinalIgnoreCase);
        // 只改了一个字段就只允许那一个字段被打红——否则相邻同型守卫会兜住变异。
        var collateral = properties
            .Where(x => !string.Equals(x, expectedProperty, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(
            collateral.Length == 0,
            $"只改了 {expectedProperty} 却连带打红 [{string.Join(", ", collateral)}]，这一格对目标规则没有鉴别力。");
    }

    private static ValidationResult Validate(IValidator validator, object instance) =>
        validator.Validate(new ValidationContext<object>(instance));

    /// <summary>从校验器**自己建出来的规则树**读长度上界，而不是读源码文本。</summary>
    private static string[] DeclaredLengthBounds<TCommand>(IServiceProvider services)
    {
        var validator = (IValidator)services.GetRequiredService<IValidator<TCommand>>();
        var rules = Assert.IsAssignableFrom<IEnumerable<IValidationRule>>(validator);
        return rules
            .SelectMany(rule => rule.Components.Select(component => (rule, component)))
            .Where(x => x.component.Validator is ILengthValidator { Max: > 0 })
            .Select(x => $"{x.rule.Member?.Name ?? x.rule.PropertyName}<="
                + ((ILengthValidator)x.component.Validator).Max)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static IProperty Property(IEntityType entityType, string propertyName) =>
        entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"WcsTask 上没有 {propertyName} 属性。");

    private static int MaxLength(IEntityType entityType, string propertyName) =>
        Property(entityType, propertyName).GetMaxLength()
            ?? throw new InvalidOperationException($"WcsTask.{propertyName} 没有声明列宽。");

    private static string ColumnName(IEntityType entityType, string propertyName) =>
        Property(entityType, propertyName).GetColumnName();

    private static WebApplicationFactory<Program> CreateHost() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));

    private static ModelFixture CreateModelFixture()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddWmsPostgreSqlPersistence("Host=localhost;Database=nerv_iip_wcs_callback_contract;Username=nerv;Password=nerv");
        return new ModelFixture(services.BuildServiceProvider());
    }

    private sealed class ModelFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;
        private readonly ApplicationDbContext dbContext;

        public ModelFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Model = dbContext.GetService<IDesignTimeModel>().Model;
        }

        public IModel Model { get; }

        public void Dispose()
        {
            dbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}

/// <summary>
/// 真库端到端那一格的门：未设 <c>NERV_IIP_TEST_POSTGRES</c> 时跳过。
/// </summary>
public sealed class WmsWcsCallbackBoundPostgresFactAttribute : FactAttribute
{
    public WmsWcsCallbackBoundPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the WCS callback bound Postgres test.";
        }
    }
}

internal sealed class WcsTaskCallbackNoopMediator : IMediator
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This test mediator only supports publish.");

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest => throw new NotSupportedException("This test mediator only supports publish.");

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This test mediator only supports publish.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This test mediator only supports publish.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This test mediator only supports publish.");
}
