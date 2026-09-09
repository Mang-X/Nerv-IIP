using System.Reflection;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.StandardOperations;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.Commands.QualityReasons;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseAssignmentReceiptAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskActionReceiptAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Coding;

using DemandPlanningDbContext = Nerv.IIP.Business.DemandPlanning.Infrastructure.ApplicationDbContext;
using ErpDbContext = Nerv.IIP.Business.Erp.Infrastructure.ApplicationDbContext;
using MasterDataDbContext = Nerv.IIP.Business.MasterData.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 网关端点级幂等键长度上界与其下游权威上界之间的机器可验关系（#3284）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：对每个带端点级 <c>MaximumLength</c> 规则的网关请求类型，
/// <c>网关端点级上界 ≤ 该端点下游的权威上界</c>——即「网关对外承诺的绝不比下游能接受的宽」。
/// 反方向（网关比下游窄，把合法键挡在门外）由 #3287 承担，本类**不**断言。</para>
///
/// <para><b>为什么不断言 <c>min(端点级规则值, 全局钳 150)</c></b>：全局钳
/// (<c>BusinessGatewayIdempotencyKey</c>) 会把一切压到 150，于是
/// <c>min(512, 150) = 150 ≤ 200</c> 对 MasterData 那处 512 恒成立——这条形式的断言**对本票要修的缺陷
/// 零鉴别力**。两个互不知情的常量偶然盖住一个洞，正是本票要拆掉的形状：任何人调大或去掉全局钳，
/// 端点级的死值立刻能打出 22001。因此这里断言的是端点级规则值本身。</para>
///
/// <para><b>枚举面从哪来（不是手写名单）</b>：网关端点级位点由**反射网关程序集**得到——
/// 枚举所有可无参构造的 <see cref="IValidator"/> 实现，实例化后读它建出来的规则，
/// 取落在 <c>IdempotencyKey</c> 成员上的 <see cref="ILengthValidator"/> 分量。
/// 新增一个带该规则的端点会自动进入值域，若未在 <see cref="DownstreamBounds"/> 登记即红
/// （<see cref="Every_gateway_endpoint_level_bound_is_covered_by_the_downstream_map"/>）。</para>
///
/// <para><b>下游权威上界怎么解析（不是手抄数字）</b>：<see cref="DownstreamBounds"/> 里登记的是
/// <c>typeof(下游命令)</c> 与 <c>(DbContext, 实体, 属性)</c> 这样的**类型引用**，
/// 具体数值在运行时从下游校验器的规则、或从下游 EF 模型的 <c>GetMaxLength()</c> 读出。
/// 下游把列宽从 128 收到 100、或把校验器上界改小，本类立刻红；改名或删除则解析失败也红
/// （<see cref="Every_declared_downstream_authority_resolves"/>）。多重权威取最小（#3281 判据）。</para>
///
/// <para><b>本类不证明什么（写清楚，避免「护栏自称完备」）</b>：</para>
/// <list type="number">
/// <item>「网关请求 → 下游命令 / 下游列」这条**链接本身是手写的**。网关是 HTTP 代理，
/// 转发目标由客户端方法里的路径字面量决定，服务内部再经 endpoint → command 一跳；
/// 两跳都不是静态可达的类型关系，静态反推需要 IL 分析、运行时反推需要起网关并逐端点发请求，
/// 都超出本票射程。链接写错的**方向**是：指向一个更宽的下游 ⇒ 假绿。链接失效（改名/删除）⇒ 红。</item>
/// <item>不证明下游校验器上界与下游列宽一致——那是各服务自己的契约
/// （Inventory 有 <c>InventoryIdempotencyKeyLengthContractTests</c>，其它服务不一定有）。</item>
/// <item>不证明 118 个带 <c>IdempotencyKey</c> 的网关请求里那 70 个**没有**端点级规则的位点安全——
/// 它们只吃全局钳，归 #3287。</item>
/// <item>不覆盖 CAP 事件信封键（<c>EventIds.Idempotency(...)</c> 产出、落 inbox 的 512/500/300 那些）：
/// 那不是网关承诺的值域。</item>
/// </list>
/// </remarks>
public sealed class BusinessGatewayIdempotencyKeyDownstreamBoundContractTests
{
    private const string IdempotencyKeyPropertyName = "IdempotencyKey";

    [Fact]
    public void Every_gateway_endpoint_level_bound_is_covered_by_the_downstream_map()
    {
        var sites = GatewayEndpointLevelBounds();

        // 值域非空：正则/反射一旦失配就会退化成空集，而 Assert.All 对空集恒真。
        Assert.NotEmpty(sites);

        var enumerated = sites.Keys.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var declared = DownstreamBounds.Keys.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        var unregistered = enumerated.Except(declared).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var stale = declared.Except(enumerated).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.True(
            unregistered.Length == 0 && stale.Length == 0,
            $"网关新增了端点级幂等键上界但未登记下游权威（{unregistered.Length}）：{string.Join(", ", unregistered)}"
            + $"；登记表里的位点在网关侧已不存在端点级上界（{stale.Length}）：{string.Join(", ", stale)}");
    }

    [Fact]
    public void Every_declared_downstream_authority_resolves()
    {
        Assert.NotEmpty(DownstreamBounds);

        foreach (var (gatewayRequestType, authorities) in DownstreamBounds)
        {
            Assert.NotEmpty(authorities);
            foreach (var authority in authorities)
            {
                var resolved = authority.Resolve();
                Assert.True(
                    resolved > 0,
                    $"{gatewayRequestType.Name} 登记的下游权威 {authority} 解析不到长度上界。");
            }
        }
    }

    [Fact]
    public void Gateway_never_promises_a_longer_idempotency_key_than_its_downstream_accepts()
    {
        var sites = GatewayEndpointLevelBounds();
        Assert.NotEmpty(sites);

        var violations = new List<string>();
        foreach (var (gatewayRequestType, gatewayBound) in sites)
        {
            var authorities = Assert.Contains(gatewayRequestType, (IDictionary<Type, DownstreamAuthority[]>)DownstreamBounds);
            var downstreamBound = authorities.Min(authority => authority.Resolve());
            if (gatewayBound > downstreamBound)
            {
                violations.Add(
                    $"{gatewayRequestType.Name}: 网关端点级上界 {gatewayBound} > 下游权威上界 {downstreamBound}"
                    + $"（权威：{string.Join(" / ", authorities.Select(x => x.ToString()))}）");
            }
        }

        Assert.Empty(violations);
    }

    // ---------------------------------------------------------------------
    // 网关侧枚举（机械）
    // ---------------------------------------------------------------------

    /// <summary>
    /// 网关程序集里所有「对 <c>IdempotencyKey</c> 施加了长度上界」的端点级校验器，
    /// 键是被校验的请求类型，值是该规则的上界。
    /// </summary>
    internal static IReadOnlyDictionary<Type, int> GatewayEndpointLevelBounds()
    {
        var assembly = typeof(Nerv.IIP.BusinessGateway.Web.Application.BusinessServices
            .BusinessConsoleSetMasterDataResourceEnabledRequest).Assembly;

        var bounds = new Dictionary<Type, int>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition || !typeof(IValidator).IsAssignableFrom(type))
            {
                continue;
            }

            var validatedType = type.GetInterfaces()
                .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(x => x.GetGenericArguments()[0])
                .FirstOrDefault();
            if (validatedType is null
                || validatedType.GetProperty(IdempotencyKeyPropertyName, BindingFlags.Instance | BindingFlags.Public)
                    ?.PropertyType != typeof(string))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            var maximum = MaximumIdempotencyKeyLength((IValidator)Activator.CreateInstance(type)!);
            if (maximum is { } value)
            {
                bounds[validatedType] = bounds.TryGetValue(validatedType, out var existing)
                    ? Math.Min(existing, value)
                    : value;
            }
        }

        return bounds;
    }

    /// <summary>
    /// 从已建好的 FluentValidation 规则里读 <c>IdempotencyKey</c> 上的长度上界。
    /// 读的是**规则本身**而不是源码文本，所以经由扩展方法（Inventory 的
    /// <c>RequiredInventoryCode</c>）或共享配置入口（Wms 的 <c>WarehouseTaskActionValidation.Configure</c>）
    /// 加上去的规则同样会被读到。
    /// </summary>
    private static int? MaximumIdempotencyKeyLength(IValidator validator)
    {
        if (validator is not IEnumerable<IValidationRule> rules)
        {
            return null;
        }

        int? maximum = null;
        foreach (var rule in rules)
        {
            var member = rule.Member?.Name ?? rule.PropertyName;
            if (!string.Equals(member, IdempotencyKeyPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var component in rule.Components)
            {
                if (component.Validator is ILengthValidator length && length.Max > 0)
                {
                    maximum = maximum is { } current ? Math.Min(current, length.Max) : length.Max;
                }
            }
        }

        return maximum;
    }

    // ---------------------------------------------------------------------
    // 下游权威（登记的是类型引用，数值运行时解析）
    // ---------------------------------------------------------------------

    private static readonly DownstreamAuthority WmsWarehouseTaskReceiptColumn =
        Column<WarehouseTaskActionReceipt>(WmsModel, nameof(WarehouseTaskActionReceipt.IdempotencyKey), "Wms");

    private static readonly DownstreamAuthority WmsAssignmentReceiptColumn =
        Column<WarehouseAssignmentReceipt>(WmsModel, nameof(WarehouseAssignmentReceipt.IdempotencyKey), "Wms");

    private static readonly DownstreamAuthority MasterDataCodeKeyColumn =
        Column<CodeIdempotencyKey>(MasterDataModel, nameof(CodeIdempotencyKey.IdempotencyKey), "MasterData");

    private static readonly DownstreamAuthority MasterDataLifecycleOperationColumn =
        Column<MasterDataLifecycleAuditEntry>(
            MasterDataModel,
            nameof(MasterDataLifecycleAuditEntry.OperationId),
            "MasterData");

    private static readonly DownstreamAuthority MesCodeKeyColumn =
        Column<CodeIdempotencyKey>(MesModel, nameof(CodeIdempotencyKey.IdempotencyKey), "Mes");

    private static readonly DownstreamAuthority ErpCodeKeyColumn =
        Column<CodeIdempotencyKey>(ErpModel, nameof(CodeIdempotencyKey.IdempotencyKey), "Erp");

    private static readonly DownstreamAuthority DemandPlanningCodeKeyColumn =
        Column<CodeIdempotencyKey>(
            DemandPlanningModel,
            nameof(CodeIdempotencyKey.IdempotencyKey),
            "DemandPlanning");

    /// <summary>
    /// 「网关请求类型 → 下游权威」的登记表。**这张表是手写的链接，数值不是**：
    /// 每一项要么指向下游命令类型（上界从它自己的校验器读），要么指向下游 EF 实体属性（上界从列宽读）。
    /// <para>登记依据是逐条实读转发链：网关 endpoint 的 <c>ForwardAsync</c> → capability client 的路径字面量
    /// → 下游服务 <c>*EndpointContracts</c> 里同路径的 endpoint → 它 <c>HandleAsync</c> 里发出的命令
    /// → 该命令的校验器 / 该命令 handler 把幂等键写进的那一列。</para>
    /// <para>下游没有任何 <c>IdempotencyKey</c> 长度规则的位点（MasterData 全部、Erp 收货、
    /// Mes 三个 CodeAllocator 写面、DemandPlanning 需求来源）只能登记列宽——
    /// 对这些位点**网关是唯一的长度防线**，<c>CodeAllocator.Normalize</c> 只 Trim、不检查长度。</para>
    /// </summary>
    private static readonly Dictionary<Type, DownstreamAuthority[]> DownstreamBounds = new()
    {
        // ---- Wms：作业动作四处 + 分配 + 三个单据完成 ----
        [typeof(BusinessConsoleStartWmsWarehouseTaskRequest)] =
            [Command<StartWarehouseTaskCommand>(), WmsWarehouseTaskReceiptColumn],
        [typeof(BusinessConsoleRecordWmsWarehouseTaskProgressRequest)] =
            [Command<RecordWarehouseTaskProgressActionCommand>(), WmsWarehouseTaskReceiptColumn],
        [typeof(BusinessConsoleReportWmsWarehouseTaskExceptionRequest)] =
            [Command<ReportWarehouseTaskExceptionCommand>(), WmsWarehouseTaskReceiptColumn],
        [typeof(BusinessConsoleCompleteWmsWarehouseTaskRequest)] =
            [Command<CompleteWarehouseTaskActionCommand>(), WmsWarehouseTaskReceiptColumn],

        // 分配命令的唯一校验器是开放泛型 WarehouseAssignmentCommandValidator<TCommand>；
        // FluentValidation 的 AssemblyScanner 跳过泛型类型定义，本仓也没有任何闭合子类，
        // 所以「它是否真的被注册执行」未经证实——这里只登记确定生效的承载列。
        [typeof(BusinessConsoleAssignWmsResourceRequest)] = [WmsAssignmentReceiptColumn],

        [typeof(BusinessConsoleCompleteWmsInboundOrderRequest)] = [Command<CompleteInboundOrderCommand>()],
        [typeof(BusinessConsoleCompleteWmsOutboundOrderRequest)] = [Command<CompleteOutboundOrderCommand>()],
        [typeof(BusinessConsoleCompleteWmsCountExecutionRequest)] = [Command<CompleteCountExecutionCommand>()],

        // ---- Inventory ----
        [typeof(BusinessConsoleConfirmStockCountAdjustmentRequest)] =
            [Command<ConfirmStockCountAdjustmentCommand>()],

        // ---- MasterData：14 个 create 走 CodeAllocator，1 个生命周期开关走审计表 ----
        [typeof(BusinessConsoleCreateProductCategoryRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateSkillRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateBusinessPartnerRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateUnitOfMeasureRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkerRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkshopRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateSiteRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateProductionLineRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkCenterRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleRegisterDeviceAssetRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateShiftRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateWorkCalendarRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateTeamRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleCreateDepartmentRequest)] = [MasterDataCodeKeyColumn],
        [typeof(BusinessConsoleSetMasterDataResourceEnabledRequest)] = [MasterDataLifecycleOperationColumn],

        // ---- Mes ----
        [typeof(BusinessConsoleMesClaimOperationTaskRequest)] = [Command<ClaimDispatchTaskCommand>()],
        [typeof(BusinessConsoleRecordProductionReportRequest)] = [Command<RecordProductionReportCommand>()],
        [typeof(BusinessConsoleMesOperationTaskActionRequest)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDefectV2Request)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesRecordDowntimeEventV2Request)] = [MesCodeKeyColumn],
        [typeof(BusinessConsoleMesSplitWorkOrderRequest)] = [Command<SplitWorkOrderCommand>()],
        [typeof(BusinessConsoleMesMergeWorkOrdersRequest)] = [Command<MergeWorkOrdersCommand>()],

        // ---- DemandPlanning ----
        [typeof(BusinessConsoleCreateOrUpdateDemandSourceRequest)] = [DemandPlanningCodeKeyColumn],
        [typeof(BusinessConsoleCreateOrUpdateForecastInputRequest)] = [Command<CreateOrUpdateForecastInputCommand>()],
        [typeof(BusinessConsoleAcceptPlanningSuggestionRequest)] = [Command<AcceptPlanningSuggestionCommand>()],

        // ---- Maintenance ----
        [typeof(BusinessConsoleAssignMaintenanceWorkOrderRequest)] = [Command<AssignMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleTransitionMaintenanceWorkOrderRequest)] = [Command<TransitionMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleCreateMaintenanceWorkOrderRequest)] = [Command<CreateMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleCreateMaintenanceWorkOrderV2Request)] = [Command<CreateMaintenanceWorkOrderV2Command>()],
        [typeof(BusinessConsoleCompleteMaintenanceWorkOrderRequest)] = [Command<CompleteMaintenanceWorkOrderCommand>()],
        [typeof(BusinessConsoleCreateMaintenancePlanRequest)] = [Command<CreateMaintenancePlanCommand>()],

        // ---- Quality ----
        [typeof(BusinessConsoleCreateQualityReasonRequest)] = [Command<CreateQualityReasonCommand>()],
        [typeof(BusinessConsoleAssignQualityInspectionTaskRequest)] = [Command<AssignInspectionTaskCommand>()],
        [typeof(BusinessConsoleClaimQualityInspectionTaskRequest)] = [Command<ClaimInspectionTaskCommand>()],
        [typeof(BusinessConsoleCreateInspectionRecordFromTaskRequest)] = [Command<CreateInspectionRecordFromTaskCommand>()],
        // 这条规则在网关侧写成多行（RuleFor / NotEmpty / MaximumLength / When 各占一行），
        // 票面与测量席位用的单行 grep 都没扫到它——本类改用反射枚举后它才现身，
        // 所以「48 个端点级位点」这个计数其实是 49。下游同为 150，本票不需要改数值，只需登记。
        [typeof(BusinessConsoleNcrDispositionRequest)] = [Command<SubmitNonconformanceReportDispositionCommand>()],

        // ---- ProductEngineering ----
        [typeof(BusinessConsoleCreateStandardOperationRequest)] = [Command<CreateStandardOperationCommand>()],

        // ---- IndustrialTelemetry（网关侧分别落在 Equipment 与 Telemetry 两个端点文件） ----
        [typeof(BusinessConsoleShelveAlarmRequest)] = [Command<ShelveAlarmCommand>()],
        [typeof(BusinessConsoleTelemetryDeviceControlCommandRequest)] = [Command<CreateDeviceControlCommandCommand>()],

        // ---- Erp ----
        [typeof(BusinessConsoleRecordErpPurchaseReceiptRequest)] = [ErpCodeKeyColumn],
    };

    private abstract class DownstreamAuthority
    {
        public abstract int Resolve();
    }

    /// <summary>下游命令自己的校验器对 <c>IdempotencyKey</c> 施加的上界。</summary>
    private sealed class CommandValidatorBound(Type commandType) : DownstreamAuthority
    {
        public override int Resolve()
        {
            foreach (var candidate in commandType.Assembly.GetTypes())
            {
                if (candidate.IsAbstract || candidate.IsGenericTypeDefinition
                    || candidate.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                var validates = candidate.GetInterfaces().Any(x =>
                    x.IsGenericType
                    && x.GetGenericTypeDefinition() == typeof(IValidator<>)
                    && x.GetGenericArguments()[0] == commandType);
                if (!validates)
                {
                    continue;
                }

                if (MaximumIdempotencyKeyLength((IValidator)Activator.CreateInstance(candidate)!) is { } bound)
                {
                    return bound;
                }
            }

            return 0;
        }

        public override string ToString() => $"validator({commandType.Name})";
    }

    /// <summary>幂等键最终落库的那一列的宽度（#3281：一个值写进多列时有效上界取最小列宽）。</summary>
    private sealed class ColumnWidthBound(Func<DbContext> contextFactory, Type entityType, string propertyName, string label)
        : DownstreamAuthority
    {
        public override int Resolve()
        {
            using var context = contextFactory();
            return context.Model.FindEntityType(entityType)?.FindProperty(propertyName)?.GetMaxLength() ?? 0;
        }

        public override string ToString() => $"column({label}.{entityType.Name}.{propertyName})";
    }

    private static DownstreamAuthority Command<TCommand>() => new CommandValidatorBound(typeof(TCommand));

    private static DownstreamAuthority Column<TEntity>(Func<DbContext> contextFactory, string propertyName, string label) =>
        new ColumnWidthBound(contextFactory, typeof(TEntity), propertyName, label);

    private static DbContext MasterDataModel() => ModelOnly<MasterDataDbContext>(
        options => new MasterDataDbContext(options, NullMediator.Instance));

    private static DbContext MesModel() => ModelOnly<MesDbContext>(
        options => new MesDbContext(options, NullMediator.Instance));

    private static DbContext ErpModel() => ModelOnly<ErpDbContext>(
        options => new ErpDbContext(options, NullMediator.Instance));

    private static DbContext DemandPlanningModel() => ModelOnly<DemandPlanningDbContext>(
        options => new DemandPlanningDbContext(options, NullMediator.Instance));

    private static DbContext WmsModel() => ModelOnly<WmsDbContext>(
        options => new WmsDbContext(options, NullMediator.Instance));

    /// <summary>
    /// 只用于读 EF 模型：不开连接、不建库。连接串必须语法合法，Npgsql 才肯建 provider。
    /// </summary>
    private static DbContext ModelOnly<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=127.0.0.1;Database=nerv_iip_gateway_idempotency_key_contract;Username=nerv;Password=nerv")
            .Options;
        return factory(options);
    }

    private sealed class NullMediator : IMediator
    {
        public static readonly NullMediator Instance = new();

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
