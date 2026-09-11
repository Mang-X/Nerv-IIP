using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;

using AppHubDbContext = Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext;
using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using MaintenanceDbContext = Nerv.IIP.Business.Maintenance.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using NotificationDbContext = Nerv.IIP.Notification.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;
using SchedulingDbContext = Nerv.IIP.Business.Scheduling.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// <see cref="IntegrationEventEnvelopeFieldBudget"/> 与它所声称的**平台 inbox 承载列**之间的
/// 机器可验关系（#3360）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：闭集里的每个信封字段，其平台预算**恰等于** 9 个服务
/// <c>processed_integration_events</c> 上对应列的**最小宽度**（#3281 判据）。
/// 数值全部在运行时从 9 个服务各自的真 EF 模型读出，**本类不手抄任何长度数字**；
/// 任一服务单边改列宽而不同步常量即红。</para>
///
/// <para><b>为什么住在 Acceptance</b>：闸装在 <c>Nerv.IIP.Messaging.CAP</c>（common 层，
/// 看不见任何服务的 EF 模型），列宽住在 9 个互不引用的服务里，只有测试侧能把它们放在一起对撞。
/// 同族先例 <c>IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests</c>（#3368）也住这里，
/// AppHub 的 ProjectReference 也是那一票加的。</para>
///
/// <para><b>⚠️ 派生链是两跳，这里只钉第二跳</b>：
/// <c>IdempotencyKey</c> 那一格的值来自 <see cref="IntegrationEventIdempotencyKey.Budget"/>，
/// 而 <c>Budget</c> 与 10 张承载列（9 张 inbox 加 <c>notification_intents.dedupe_key</c>）的关系
/// 由 <c>IntegrationEventEnvelopeIdempotencyKeyBudgetContractTests</c> 钉住。
/// 本类**额外**把它与 inbox 那一列再对一次（<see cref="Every_budgeted_field_equals_the_narrowest_inbox_column"/>
/// 对四个字段一视同仁），因此两条断言在这一格上**互为交叉验证**，
/// 而 <c>notification_intents</c> 那一列只由上一票那条覆盖。删任一条都会让覆盖面变窄。</para>
///
/// <para><b>本类不证明什么（值域边界，别读成完备）</b>：</para>
/// <list type="number">
/// <item><b>不证明闭集穷举了「应当受闸约束」的信封字段。</b>
/// <c>CorrelationId</c> / <c>CausationId</c> / <c>OrganizationId</c> / <c>EnvironmentId</c> /
/// <c>Actor</c> 不在闭集里，是因为 inbox **根本不存这些字段**（由
/// <see cref="No_unbudgeted_envelope_field_is_carried_by_the_inbox_table"/> 实测钉住，
/// 不是靠人读）。将来有人往 inbox 加一列承载其中某个字段，那条断言会红。</item>
/// <item><b>不证明这四列是该值的全部承载列。</b>某个**服务专属**的写入点若把同一个值落进更窄的列，
/// 有效上界按 #3281 取最小，责任在那个服务 —— 同 #3339 处置
/// <c>inspection_tasks.trigger_idempotency_key</c>(474) 的判例。</item>
/// <item><b>⚠️ <see cref="InboxOwners"/> 那份 9 个服务的名单本身是**声明式白名单**，不由任何东西派生。</b>
/// 「字段维度」有反向断言看守（<see cref="No_unbudgeted_envelope_field_is_carried_by_the_inbox_table"/>
/// 会在 inbox 冒出闭集外的信封字段时报红），但**「服务维度」没有**：
/// 将来新增第 10 个拥有 <c>processed_integration_events</c> 的服务，
/// 它若把某一列开得比 <see cref="IntegrationEventEnvelopeFieldBudget"/> 更窄，
/// **本类一条都不会红**（名单里没有它，<c>Min</c> 自然算不到它）。
/// 这是本仓「白名单记的是写名单那刻的世界」的同形边界，**登记在此，不假装它不存在**。
/// ⛔ 不用源码文本扫描去补（#3176 / PR #3214 三轮实证不收敛）。</item>
/// <item><b>不证明所有消费者都走 <c>IntegrationEventConsumerGuard</c>。</b>绕开 Guard 的不受闸保护。</item>
/// </list>
/// </remarks>
public sealed class IntegrationEventEnvelopeFieldBudgetContractTests
{
    private const string InboxTable = "processed_integration_events";

    private static (string Service, Func<DbContext> Factory)[] InboxOwners() =>
    [
        ("AppHub", ModelOnly<AppHubDbContext>),
        ("Notification", ModelOnly<NotificationDbContext>),
        ("Wms", ModelOnly<WmsDbContext>),
        ("Quality", ModelOnly<QualityDbContext>),
        ("Scheduling", ModelOnly<SchedulingDbContext>),
        ("Mes", ModelOnly<MesDbContext>),
        ("DemandPlanning", ModelOnly<DemandPlanningDbContext>),
        ("Maintenance", ModelOnly<MaintenanceDbContext>),
        ("Erp", ModelOnly<ErpDbContext>),
    ];

    [Fact]
    public void Every_inbox_owner_resolves_the_inbox_entity()
    {
        var unresolved = new List<string>();
        foreach (var (service, factory) in InboxOwners())
        {
            using var context = factory();
            if (InboxEntity(context) is null)
            {
                unresolved.Add(service);
            }
        }

        Assert.True(
            unresolved.Count == 0,
            $"这些服务解析不到 {InboxTable} 实体（登记表已与模型漂移）：{string.Join(", ", unresolved)}");
    }

    /// <summary>
    /// 验收 1：每个受闸字段的预算 == 9 个服务该列宽度的最小值。
    /// 任一服务把列改宽或改窄而不同步常量，这条就红。
    /// </summary>
    [Fact]
    public void Every_budgeted_field_equals_the_narrowest_inbox_column()
    {
        var readings = ColumnWidthsByField();
        var drift = new List<string>();

        foreach (var (fieldName, budget) in IntegrationEventEnvelopeFieldBudget.ByFieldName)
        {
            Assert.True(
                readings.TryGetValue(fieldName, out var widths) && widths.Count == InboxOwners().Length,
                $"字段 {fieldName} 没能在全部 {InboxOwners().Length} 个服务的 {InboxTable} 上解析到列宽，断言会退化成空转。");

            var narrowest = widths!.Values.Min();
            if (narrowest != budget)
            {
                drift.Add($"{fieldName}: 预算={budget} 最窄列={narrowest} 逐服务读数=[{string.Join(", ", widths.Select(x => $"{x.Key}={x.Value}"))}]");
            }
        }

        Assert.True(drift.Count == 0, "平台预算与 inbox 列宽已漂移：\n" + string.Join("\n", drift));
    }

    /// <summary>
    /// 闭集的**反向**断言：inbox 上承载的信封字段，**一个都不能**落在闭集之外。
    /// <para>没有这一条，「不在闭集里 = 声明放弃」就只是一句注释；有了它，
    /// 将来有人往 inbox 加一列承载 <c>CorrelationId</c> 之类字段时会立刻红，
    /// 而不是静默多出一条不受闸保护的承载列。</para>
    /// </summary>
    [Fact]
    public void No_unbudgeted_envelope_field_is_carried_by_the_inbox_table()
    {
        var envelopeFieldNames = typeof(IIntegrationEventEnvelope)
            .GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var escaped = new List<string>();
        foreach (var (service, factory) in InboxOwners())
        {
            using var context = factory();
            var entity = InboxEntity(context);
            Assert.True(entity is not null, $"{service} 解析不到 {InboxTable} 实体。");

            foreach (var property in entity!.GetProperties())
            {
                if (envelopeFieldNames.Contains(property.Name) &&
                    !IntegrationEventEnvelopeFieldBudget.ByFieldName.ContainsKey(property.Name))
                {
                    escaped.Add($"{service}.{InboxTable}.{property.Name}");
                }
            }
        }

        Assert.True(
            escaped.Count == 0,
            "inbox 承载了闭集之外的信封字段，它们不受长度闸保护、会退回 poison：\n" + string.Join("\n", escaped));
    }

    /// <summary>
    /// <c>IdempotencyKey</c> 那一格与 producer 侧预算是**同一个量的两侧**，必须恒等
    /// （闸比预算严 ⇒ producer 合规产出的键被判假死信；宽 ⇒ 超界键照样 poison、闸形同虚设）。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>这条断言比它看起来弱，说清楚</b>：两边今天都是 <c>const</c> 且被测常量就写成
    /// <c>= IntegrationEventIdempotencyKey.Budget</c>，所以它**抓得到**「有人改成一个数值不同的手抄常量」，
    /// **抓不到**「有人改成数值恰好相同的手抄常量（<c>= 512</c>）」—— 那种改动会静默通过，
    /// 并把「由构造恒等」悄悄降级成「今天碰巧同值」。
    /// 真正让恒等**由构造成立**的是源码里那一处引用，而本仓⛔不建源码文本扫描护栏（#3176 / PR #3214），
    /// 所以这一段**只能由人读守住**。别把这条读成「恒等已被机器钉死」。
    /// </remarks>
    [Fact]
    public void The_idempotency_key_gate_equals_the_producer_side_budget()
    {
        Assert.Equal(
            IntegrationEventIdempotencyKey.Budget,
            IntegrationEventEnvelopeFieldBudget.ByFieldName[nameof(IIntegrationEventEnvelope.IdempotencyKey)]);
    }

    private static Dictionary<string, Dictionary<string, int>> ColumnWidthsByField()
    {
        var readings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (service, factory) in InboxOwners())
        {
            using var context = factory();
            var entity = InboxEntity(context);
            if (entity is null)
            {
                continue;
            }

            foreach (var fieldName in IntegrationEventEnvelopeFieldBudget.ByFieldName.Keys)
            {
                if (entity.FindProperty(fieldName)?.GetMaxLength() is { } width)
                {
                    if (!readings.TryGetValue(fieldName, out var perService))
                    {
                        perService = new Dictionary<string, int>(StringComparer.Ordinal);
                        readings[fieldName] = perService;
                    }

                    perService[service] = width;
                }
            }
        }

        return readings;
    }

    /// <summary>按**物理表名**解析（9 个服务各有一份同名 CLR 类型，表名才是这张表在库里的身份）。</summary>
    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType? InboxEntity(DbContext context)
    {
        var entities = context.Model.GetEntityTypes()
            .Where(entity => string.Equals(entity.GetTableName(), InboxTable, StringComparison.Ordinal))
            .ToArray();

        return entities.Length == 1 ? entities[0] : null;
    }

    private static DbContext ModelOnly<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=127.0.0.1;Database=nerv_iip_envelope_field_budget_contract;Username=nerv;Password=nerv")
            .Options;
        return (DbContext)Activator.CreateInstance(typeof(TContext), options, NullMediatorForFieldBudget.Instance)!;
    }

    private sealed class NullMediatorForFieldBudget : IMediator
    {
        public static readonly NullMediatorForFieldBudget Instance = new();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new NotSupportedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
