using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.DemandPlanning.Domain;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.ProductEngineering.Domain;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Coding;
using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using MaintenanceDbContext = Nerv.IIP.Business.Maintenance.Infrastructure.ApplicationDbContext;
using MasterDataDbContext = Nerv.IIP.Business.MasterData.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using ProductEngineeringDbContext = Nerv.IIP.Business.ProductEngineering.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #3307：<see cref="CodeIdempotencyKey"/> 是共享实体，但它的 EF 配置改前在 **7 个服务里逐字节复制**，
/// 7 份列宽可以单边漂移且没有任何机制会红。本 PR 已把那 7 份删除、配置收进
/// <see cref="CodingModelBuilderExtensions.ConfigureCodingEntities"/>，列宽唯一出处是
/// <see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/>。
///
/// <para><b>这个类看守的是收拢**剩下的**那条失效方向，不是「7 份互相一致」。</b>
/// 「副本互相一致」那种断言两侧都是手抄值，7 份被同样改坏时仍然全绿，本仓已有判例，不采用。
/// 收拢之后 7 份之间不再有可漂移的字面量，真正剩下的风险是：
/// <list type="number">
/// <item>某个服务**不调**那个扩展方法（<c>ApplyConfigurationsFromAssembly</c> 按定义只扫服务自己的程序集，
/// 配置搬进共享包后就不在它的扫描面上），于是该实体退回 EF 约定——表名、列名、列宽全变，编译期零报错。
/// 这不是假想：Maintenance 就没调 <c>ConfigureIntegrationEventDeadLetters()</c> 而是留了一份服务本地副本。</item>
/// <item>某个服务再写一份服务本地配置盖掉共享配置。</item>
/// <item>新服务开始持有这个共享实体，却没人想起把它接进来。</item>
/// </list>
/// 这三条都是**「某一侧偏离唯一出处」**，断言的两侧一侧是单一常量、另一侧是该服务**真实的 EF 模型**
/// （migration 正是从这个模型生成的），不是两份手抄值互比。</para>
///
/// <para><b>本测试项目是本仓唯一能写这句话的地方</b>：它的 csproj 有 16 条 <c>ProjectReference</c>
/// （13 个业务服务 + BusinessGateway + Notification + AppHub，实读），
/// 因而能在同一个进程里读到 7 个服务各自的 EF 模型。单服务的测试项目做不到——
/// PR #3303 的 <c>ErpCodingIdempotencyKeyLengthContractTests</c> 只能证明 Erp 那一份没被改，
/// 另外 6 份任意一份漂移它一格都不会红，那正是本票承接的面。</para>
///
/// <para><b>受管服务集合是枚举出来的，不是手写白名单。</b>
/// <see cref="DiscoverContextTypesOwningTheSharedEntity"/> 从本测试程序集出发**沿引用拓扑**传递加载所有
/// <c>Nerv.IIP.*</c> 程序集，枚举其中全部非抽象 <see cref="DbContext"/> 子类，取出声明了
/// <c>DbSet&lt;CodeIdempotencyKey&gt;</c> 的那些，再与下面显式构造的 7 个对撞。
/// 第 8 个服务开始持有这个实体时，<see cref="Governed_service_set_is_closed_over_the_reference_topology"/>
/// 会红并点名，而不是静默把它漏掉（本仓「白名单选取会静默漏掉后来者」判例）。</para>
///
/// <para><b>合同分类</b>（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是各服务 migration 里
/// <c>code_idempotency_keys.idempotency_key</c> 的物理类型 <c>character varying(150)</c>
/// （7 份 <c>ApplicationDbContextModelSnapshot.cs</c> 实读 7/7）；
/// <c>Regression</c> 的权威来源是 GitHub #3307 的验收条件。</para>
///
/// <para><b>值域边界（声明放弃了什么，别读成完备）</b>：
/// <list type="bullet">
/// <item>本类**不**证明「列宽 150 这个数字是对的」。改
/// <see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/> 一处会同时改动 7 个服务的模型，
/// 断言两侧同步移动，本类**恒绿**——这是收拢的定义，不是漏洞。列宽取值的权威本来就是 migration：
/// 改常量会让 7 份 model snapshot 全部与模型不符，由各服务 <c>MigrateAsync</c> 内的
/// <c>PendingModelChangesWarning</c> 在 <c>PostgreSQL Provider Tests</c> 上显形。
/// 本仓**没有** pending-model-changes 门禁（<c>HasPendingModelChanges</c> 全仓零调用点，#3347 实读），
/// 别照抄别处那句「由 pending-model-changes 门禁承担」。</item>
/// <item>闭集的成立机制是**本测试项目的引用拓扑**，不是包归属。某个新服务如果没有被
/// <c>Nerv.IIP.Business.Acceptance.Tests.csproj</c> 引用，它就不在遍历面上——
/// 失效方向是**假绿**。
/// 今天在引用表里的是 **13 个业务服务 + BusinessGateway + Notification + AppHub**（16 条 <c>ProjectReference</c>，实读）。
/// <b>明确不在遍历面上的 DbContext 宿主有三个：<c>Iam</c> / <c>FileStorage</c> / <c>Ops</c></b>——
/// 本测试项目只引用它们的 <c>Contracts.*</c> 与 <c>Sdk.*</c>，不引用其 <c>*.Infrastructure</c>
/// （实读：这三个 <c>*.Infrastructure.dll</c> 都不出现在本测试项目的输出目录里，而受管的 7 个都在）。
/// 今天它们**都不引用 <c>Nerv.IIP.Coding</c>**（csproj 面零命中）所以没有实际漏网，
/// 但**别把这句读成「全仓都在面上」**：这三个若开始持有本共享实体，本类不会红，失效方向是**假绿**，
/// 届时要把它们的 Web 项目加进本测试项目的引用表。</item>
/// <item>读的是 **EF 模型**而不是 migration 脚本文本。模型与迁移单边漂移不由本类抓。</item>
/// <item>本类只管这一个共享实体的这一列。全仓其它 <c>idempotency_key</c> 列
/// （BarcodeLabel / Inventory / Wms 的 128、消费者收件箱的 512、死信箱的 500 等）
/// 与本实体不同族、不同所有者，**不在射程内**。</item>
/// </list></para>
/// </summary>
public sealed class CodeIdempotencyKeyCrossServiceWidthContractTests
{
    private const string Table = "code_idempotency_keys";
    private const string Column = "idempotency_key";

    /// <summary>改前 7 份副本各自手抄的列宽。7/7 实读一致——缺陷是「可以漂移」，不是「已经漂了」。</summary>
    private const int PreChangeDuplicatedWidth = 150;

    /// <summary>
    /// 改前逐字节复制的份数。**这是冻结的历史事实，永远不该变**——
    /// 它描述 #3307 改前那 7 份副本，与「今天有几个服务受管」不是同一件事。
    /// </summary>
    private const int PreChangeDuplicateCount = 7;

    /// <summary>
    /// **今天**受管的服务数，即 <see cref="GovernedServices"/> 显式列出的条数。
    /// 与 <see cref="PreChangeDuplicateCount"/> 今天同为 7 只是巧合：
    /// 合法新增第 8 个持有本共享实体的服务时**该改的是这一个**，
    /// ⛔ 不要去改那个写着「改前份数」的历史常量。
    /// </summary>
    private const int GovernedServiceCount = 7;

    private static IEnumerable<(string Service, Func<DbContext> Factory)> GovernedServices()
    {
        yield return ("DemandPlanning", () => new DemandPlanningDbContext(Options<DemandPlanningDbContext>(DemandPlanningFacts.Schema), NoopMediator.Instance));
        yield return ("Erp", () => new ErpDbContext(Options<ErpDbContext>(ErpFacts.Schema), NoopMediator.Instance));
        yield return ("Maintenance", () => new MaintenanceDbContext(Options<MaintenanceDbContext>(MaintenanceFacts.Schema), NoopMediator.Instance));
        yield return ("MasterData", () => new MasterDataDbContext(Options<MasterDataDbContext>(MasterDataFacts.Schema), NoopMediator.Instance));
        yield return ("Mes", () => new MesDbContext(Options<MesDbContext>(MesFacts.Schema), NoopMediator.Instance));
        yield return ("ProductEngineering", () => new ProductEngineeringDbContext(Options<ProductEngineeringDbContext>(ProductEngineeringFacts.Schema), NoopMediator.Instance));
        yield return ("Quality", () => new QualityDbContext(Options<QualityDbContext>(QualityFacts.Schema), NoopMediator.Instance));
    }

    /// <summary>
    /// 每个受管服务**真实 EF 模型**里的那一列，宽度必须等于唯一出处常量，且确实落在
    /// <c>code_idempotency_keys</c> 这张表上。
    ///
    /// <para>表名一并断言不是凑数：服务忘调 <see cref="CodingModelBuilderExtensions.ConfigureCodingEntities"/> 时，
    /// EF 约定会给出 <c>CodeIdempotencyKeys</c> 这样的表名和无上界的列——
    /// 只断言列宽的话，那种情况下 <c>GetMaxLength()</c> 返回 <c>null</c> 也会红，
    /// 但报出来的原因会指向「宽度不符」而不是真因。</para>
    /// </summary>
    [Fact]
    public void Every_governed_service_carries_the_single_source_column_width()
    {
        var readings = new List<(string Service, string? Table, int? Width)>();
        foreach (var (service, factory) in GovernedServices())
        {
            using var dbContext = factory();
            var entityType = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CodeIdempotencyKey));
            Assert.True(
                entityType is not null,
                $"{service} 的 EF 模型里没有 {nameof(CodeIdempotencyKey)}；该服务声明了 DbSet 却没把共享实体接进模型。");

            var property = entityType!.GetProperties().SingleOrDefault(
                candidate => string.Equals(candidate.GetColumnName(), Column, StringComparison.Ordinal));
            Assert.True(
                property is not null,
                $"{service} 的 {nameof(CodeIdempotencyKey)} 上没有 {Column} 列；"
                + $"多半是没调 {nameof(CodingModelBuilderExtensions.ConfigureCodingEntities)}，实体退回了 EF 约定。");

            readings.Add((service, entityType.GetTableName(), property!.GetMaxLength()));
        }

        Assert.Equal(GovernedServiceCount, readings.Count);
        Assert.All(
            readings,
            reading =>
            {
                Assert.Equal(Table, reading.Table);
                Assert.Equal(CodeIdempotencyKey.IdempotencyKeyMaxLength, reading.Width);
            });
    }

    /// <summary>
    /// 受管集合必须**闭合于引用拓扑**：沿本测试程序集的引用图枚举出来的、持有
    /// <c>DbSet&lt;CodeIdempotencyKey&gt;</c> 的 <see cref="DbContext"/> 集合，
    /// 必须与 <see cref="GovernedServices"/> 显式列出的那 7 个逐个相等。
    /// 多一个（新服务开始用共享编码实体）或少一个（某服务撤掉 DbSet）都红。
    /// </summary>
    [Fact]
    public void Governed_service_set_is_closed_over_the_reference_topology()
    {
        var discovered = DiscoverContextTypesOwningTheSharedEntity();
        var asserted = GovernedServices()
            .Select(entry =>
            {
                using var dbContext = entry.Factory();
                return dbContext.GetType();
            })
            .OrderBy(type => type.Assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(GovernedServiceCount, asserted.Length);
        Assert.Equal(
            discovered.Select(type => type.Assembly.GetName().Name).ToArray(),
            asserted.Select(type => type.Assembly.GetName().Name).ToArray());
    }

    /// <summary>
    /// 缺陷不是假想的：改前 7 份副本各自手抄 <c>HasMaxLength(150)</c>，本读数把「收拢是零 schema 变化」
    /// 写死——共享常量今天的值必须仍等于改前那 7 份的手抄值。
    /// 这条是本类唯一一条「常量值」断言，它钉的是**迁移兼容**，不是「150 这个数字合理」。
    /// </summary>
    [Fact]
    public void Collapsing_the_duplicates_did_not_change_the_width()
    {
        Assert.Equal(PreChangeDuplicatedWidth, CodeIdempotencyKey.IdempotencyKeyMaxLength);
    }

    private static DbContextOptions<TContext> Options<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(
                "Host=localhost;Database=nerv_iip_code_idempotency_key_contract;Username=nerv;Password=nerv",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;

    private static Type[] DiscoverContextTypesOwningTheSharedEntity()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        var assemblies = new List<Assembly>();
        pending.Enqueue(typeof(CodeIdempotencyKeyCrossServiceWidthContractTests).Assembly);

        while (pending.Count > 0)
        {
            var assembly = pending.Dequeue();
            if (!visited.Add(assembly.GetName().Name!))
            {
                continue;
            }

            assemblies.Add(assembly);
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("Nerv.IIP.", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                pending.Enqueue(Assembly.Load(reference));
            }
        }

        return assemblies
            .SelectMany(LoadableTypes)
            .Where(type => typeof(DbContext).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.PropertyType == typeof(DbSet<CodeIdempotencyKey>)))
            .Distinct()
            .OrderBy(type => type.Assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        internal static readonly NoopMediator Instance = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This contract test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");
    }
}
