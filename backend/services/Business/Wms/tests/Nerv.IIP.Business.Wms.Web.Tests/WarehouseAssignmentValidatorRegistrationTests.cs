using System.Reflection;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using FluentValidation.Validators;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// 受控分配家族命令层入参校验的**真实可达性**契约（#3291）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：实现 <see cref="IWarehouseAssignmentCommand"/> 的每一条命令，
/// 都能从**真实 host 容器**解析出校验器；该校验器的 9 条规则逐条对每条命令生效；
/// 且这些规则确实在 MediatR 管道里跑到（不是只挂在容器里）。</para>
///
/// <para><b>为什么需要这条</b>：修复前这 9 条规则写在
/// <c>sealed class WarehouseAssignmentCommandValidator&lt;TCommand&gt; : AbstractValidator&lt;TCommand&gt;</c> 上。
/// sealed 开放泛型无法派生闭合，全仓零引用，唯一注册路径
/// <c>Program.cs</c> 的 <c>AddValidatorsFromAssembly</c> 不注册泛型定义——
/// 实测真实 host 里 5 条命令的 <c>IValidator&lt;C&gt;</c> 解析数均为 0，
/// 而正对照 <c>StartWarehouseTaskCommand</c> 解析数为 1。
/// 这个缺陷**编译期完全无声**：类照样编译、规则照样写得整整齐齐。
/// 所以这里断言的落点必须是「容器解析得到」与「管道里跑到」，而不是「源码里有这些 RuleFor」。</para>
///
/// <para><b>两层值域都不是手写名单</b>：</para>
/// <list type="number">
/// <item><b>命令值域</b>由**反射** Wms.Web 程序集里 <see cref="IWarehouseAssignmentCommand"/>
/// 的全部具体实现得到。<see cref="Fixtures"/> 只提供各命令的构造夹具，其键集必须与反射集合逐一相等
/// （<see cref="Every_assignment_command_has_a_fixture"/>），
/// 新增第 6 条分配命令而不给它校验器或夹具都会红。</item>
/// <item><b>规则分量值域</b>由**校验器自己建出来的规则树**得到：遍历
/// <see cref="IValidationRule"/> 及其 <see cref="IRuleComponent"/>，
/// 键取 <c>(成员, 是否集合元素规则, 分量校验器名)</c>。
/// <see cref="Every_rule_discriminates_on_every_assignment_command"/> 断言
/// 「声明出来的分量集合」与「15 个违例格实际打红的分量集合」**双向相等**。
/// ⇒ 删掉任意一个违例格 ⇒ 有分量没被打红 ⇒ 红；
/// 新增一条规则而不补格 ⇒ 同样红。覆盖面不再是手写名单。</item>
/// </list>
/// <para><b>为什么必须细到「分量」而不是「成员」</b>：成员级覆盖挡不住删格——
/// 删掉「OrganizationId 空」之后「OrganizationId 超 100」仍然覆盖着同一个成员，
/// 名单缩水而门禁照绿。分量级把 <c>NotEmptyValidator</c> 与 <c>MaximumLengthValidator</c>
/// 当成两个必须各自被打红的位点。</para>
///
/// <para><b>本类不证明什么</b>：不证明网关侧上界与这里一致（那是
/// <c>BusinessGatewayIdempotencyKeyDownstreamBoundContractTests</c> 的射程）；
/// 不证明这些上界与落库列宽一致（分配 receipt 表存的是 <c>WmsText.IdempotencyKey</c>
/// 的 SHA256 派生值，列宽对原始键零约束）；
/// 端到端那条只对每条命令各跑一格违例 + 一格异字段对照，
/// 逐字段的鉴别力由 <see cref="Every_rule_discriminates_on_every_assignment_command"/> 承担。</para>
///
/// <para><b>「× 5 条命令」这一维的真实鉴别力（写明口径，别把 120 格读成 120 份）</b>：
/// 5 条命令共用同一份 <c>WarehouseAssignmentValidation.Configure&lt;TCommand&gt;</c>，
/// 所以对**规则级**变异（改上界、删规则）这 5 份是**等价输入**——
/// 规则级鉴别力只有 1 份，实测 M6–M10 每格红数均为 1 即此故。
/// 这一维非等价的只有一条窄面：某条命令的校验器**存在但体内没调 <c>Configure</c>**
/// （或调了别的规则集），那时只有它那 24 格会红。
/// 保留 <c>foreach</c> 的代价是一行，收益是这条窄面，但不得据此宣称 120 份独立鉴别力。</para>
/// </remarks>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WarehouseAssignmentValidatorRegistrationTests
{
    /// <summary>全字段合法的基线入参。任何单字段改坏都应当且只应当打红对应字段。</summary>
    private static readonly AssignmentInput Valid = new(
        "org-001",
        "env-dev",
        "principal-001",
        ["SITE-01"],
        "POOL-A",
        "op-001",
        "assign-idem-001",
        1);

    private static readonly IReadOnlyDictionary<Type, Func<AssignmentInput, object>> Fixtures =
        new Dictionary<Type, Func<AssignmentInput, object>>
        {
            [typeof(AssignInboundOrderCommand)] = input => new AssignInboundOrderCommand(
                new InboundOrderId(Guid.CreateVersion7()),
                input.OrganizationId,
                input.EnvironmentId,
                input.AssignerPrincipalId,
                input.AuthorizedSiteCodes,
                input.PoolCode,
                input.OperatorPrincipalId,
                input.IdempotencyKey,
                input.ExpectedVersion),
            [typeof(AssignPutawayTaskCommand)] = input => new AssignPutawayTaskCommand(
                new WarehouseTaskId(Guid.CreateVersion7()),
                input.OrganizationId,
                input.EnvironmentId,
                input.AssignerPrincipalId,
                input.AuthorizedSiteCodes,
                input.PoolCode,
                input.OperatorPrincipalId,
                input.IdempotencyKey,
                input.ExpectedVersion),
            [typeof(AssignOutboundOrderCommand)] = input => new AssignOutboundOrderCommand(
                new OutboundOrderId(Guid.CreateVersion7()),
                input.OrganizationId,
                input.EnvironmentId,
                input.AssignerPrincipalId,
                input.AuthorizedSiteCodes,
                input.PoolCode,
                input.OperatorPrincipalId,
                input.IdempotencyKey,
                input.ExpectedVersion),
            [typeof(AssignPickingTaskCommand)] = input => new AssignPickingTaskCommand(
                new WarehouseTaskId(Guid.CreateVersion7()),
                input.OrganizationId,
                input.EnvironmentId,
                input.AssignerPrincipalId,
                input.AuthorizedSiteCodes,
                input.PoolCode,
                input.OperatorPrincipalId,
                input.IdempotencyKey,
                input.ExpectedVersion),
            [typeof(AssignCountExecutionCommand)] = input => new AssignCountExecutionCommand(
                new CountExecutionId(Guid.CreateVersion7()),
                input.OrganizationId,
                input.EnvironmentId,
                input.AssignerPrincipalId,
                input.AuthorizedSiteCodes,
                input.PoolCode,
                input.OperatorPrincipalId,
                input.IdempotencyKey,
                input.ExpectedVersion),
        };

    /// <summary>
    /// 9 条规则的 15 格违例。每格只改一个字段，并声明它应当打红的属性名。
    /// </summary>
    /// <remarks>
    /// 「只改一个字段」是必须的：夹具同时触犯多条规则时，删掉目标那条仍会因为别的规则而红，
    /// 变异杀不掉、这一格的鉴别力为 0。
    /// </remarks>
    private static readonly RuleCase[] InvalidCases =
    [
        new("OrganizationId 空", "OrganizationId", input => input with { OrganizationId = "" }),
        new("OrganizationId 超 100", "OrganizationId", input => input with { OrganizationId = new string('o', 101) }),
        new("EnvironmentId 空", "EnvironmentId", input => input with { EnvironmentId = "" }),
        new("EnvironmentId 超 100", "EnvironmentId", input => input with { EnvironmentId = new string('e', 101) }),
        new("AssignerPrincipalId 空", "AssignerPrincipalId", input => input with { AssignerPrincipalId = "" }),
        new("AssignerPrincipalId 超 150", "AssignerPrincipalId", input => input with { AssignerPrincipalId = new string('a', 151) }),
        new("AuthorizedSiteCodes 空集合", "AuthorizedSiteCodes", input => input with { AuthorizedSiteCodes = [] }),
        new("AuthorizedSiteCodes 元素空", "AuthorizedSiteCodes[0]", input => input with { AuthorizedSiteCodes = [""] }),
        new("AuthorizedSiteCodes 元素超 100", "AuthorizedSiteCodes[0]", input => input with { AuthorizedSiteCodes = [new string('s', 101)] }),
        new("PoolCode 空", "PoolCode", input => input with { PoolCode = "" }),
        new("PoolCode 超 150", "PoolCode", input => input with { PoolCode = new string('p', 151) }),
        new("OperatorPrincipalId 超 150", "OperatorPrincipalId", input => input with { OperatorPrincipalId = new string('u', 151) }),
        new("IdempotencyKey 空", "IdempotencyKey", input => input with { IdempotencyKey = "" }),
        new("IdempotencyKey 超 128", "IdempotencyKey", input => input with { IdempotencyKey = new string('k', 129) }),
        new("ExpectedVersion 非正", "ExpectedVersion", input => input with { ExpectedVersion = 0 }),
    ];

    /// <summary>
    /// 各上界的**恰好取到**那一格，必须整体合法。
    /// </summary>
    /// <remarks>
    /// 没有这组，把 <c>MaximumLength(100)</c> 改成 <c>MaximumLength(1)</c> 之类的收紧变异不会被杀——
    /// 违例组只证明「超界会红」，证不到「界在哪」。
    /// <c>OperatorPrincipalId = null</c> 一格同时钉住它是可选字段（只有 MaximumLength、没有 NotEmpty）。
    /// </remarks>
    private static readonly RuleCase[] AtBoundCases =
    [
        new("OrganizationId 恰 100", "", input => input with { OrganizationId = new string('o', 100) }),
        new("EnvironmentId 恰 100", "", input => input with { EnvironmentId = new string('e', 100) }),
        new("AssignerPrincipalId 恰 150", "", input => input with { AssignerPrincipalId = new string('a', 150) }),
        new("AuthorizedSiteCodes 元素恰 100", "", input => input with { AuthorizedSiteCodes = [new string('s', 100)] }),
        new("PoolCode 恰 150", "", input => input with { PoolCode = new string('p', 150) }),
        new("OperatorPrincipalId 恰 150", "", input => input with { OperatorPrincipalId = new string('u', 150) }),
        new("OperatorPrincipalId 缺省", "", input => input with { OperatorPrincipalId = null }),
        new("IdempotencyKey 恰 128", "", input => input with { IdempotencyKey = new string('k', 128) }),
        new("ExpectedVersion 恰 1", "", input => input with { ExpectedVersion = 1 }),
    ];

    /// <summary>
    /// 各长度上界的**钉死值**。数值来自承载列宽这一独立第二来源，不是从校验器抄回来的。
    /// </summary>
    /// <remarks>
    /// <para>逐条与 <c>WmsEntityTypeConfigurations.cs</c> 里
    /// <c>warehouse_assignment_receipts</c> 的承载列宽精确相等：
    /// <c>PoolCode</c>/<c>pool_code</c>=150、<c>OperatorPrincipalId</c>/<c>operator_principal_id</c>=150、
    /// <c>AssignerPrincipalId</c>/<c>assigned_by_principal_id</c>=150、
    /// <c>AuthorizedSiteCodes</c> 每项/<c>site_code</c>=100、
    /// <c>IdempotencyKey</c>/<c>idempotency_key</c>=128。
    /// <c>OrganizationId</c>/<c>EnvironmentId</c> 的 100 与全平台租户列宽一致。
    /// ⇒ 这些数不是「凭空钉的」，也不是「唯一来源是从未执行过的源码」：schema 是独立来源。</para>
    /// <para><b>为什么要有这张表</b>：<see cref="AtBoundCases"/> 只能钉住**今天已有**的那几条长度规则；
    /// 新加一条 <c>MaximumLength</c> 而不补 at-bound 格时，分量覆盖断言会逼作者补违例格，
    /// 却不强制他补上界格，数值就会没人钉。这张表与规则树**双向相等**比对
    /// （<see cref="Declared_length_bounds_match_the_pinned_table"/>），新增/删除/改值三种方向都红。</para>
    /// </remarks>
    private static readonly LengthBound[] PinnedLengthBounds =
    [
        new("OrganizationId", false, 100),
        new("EnvironmentId", false, 100),
        new("AssignerPrincipalId", false, 150),
        new("AuthorizedSiteCodes", true, 100),
        new("PoolCode", false, 150),
        new("OperatorPrincipalId", false, 150),
        new("IdempotencyKey", false, 128),
    ];

    /// <summary>
    /// <see cref="Fixtures"/> 的键集必须**恰好**等于反射出的分配命令集合。
    /// </summary>
    /// <remarks>
    /// 手写名单会静默漏掉后来者：新增第 6 条分配命令时，若这里按名单跑，
    /// 它不进值域、下面两条断言对它恒真。所以值域从类型系统枚举，名单只负责提供构造夹具。
    /// </remarks>
    [Fact]
    public void Every_assignment_command_has_a_fixture()
    {
        var reflected = AssignmentCommandTypes();

        Assert.NotEmpty(reflected);
        Assert.Equal(
            reflected.Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal),
            Fixtures.Keys.Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_assignment_command_resolves_a_validator_from_the_real_host()
    {
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();

        var reflected = AssignmentCommandTypes();
        Assert.NotEmpty(reflected);

        var unregistered = new List<string>();
        foreach (var commandType in reflected)
        {
            var resolved = scope.ServiceProvider
                .GetServices(typeof(IValidator<>).MakeGenericType(commandType))
                .ToArray();
            if (resolved.Length == 0)
            {
                unregistered.Add(commandType.FullName!);
            }
        }

        Assert.True(
            unregistered.Count == 0,
            "以下分配命令在真实 host 容器里解析不到任何校验器，它们的命令层入参校验整体不可达（#3291）：\n"
            + string.Join("\n", unregistered));
    }

    [Fact]
    public async Task Every_rule_discriminates_on_every_assignment_command()
    {
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();

        var failures = new List<string>();
        foreach (var commandType in AssignmentCommandTypes())
        {
            var validator = (IValidator)scope.ServiceProvider
                .GetRequiredService(typeof(IValidator<>).MakeGenericType(commandType));
            var build = Fixtures[commandType];

            // 覆盖面的值域由**校验器自己建出来的规则树**给出，不是这个文件里的名单。
            var declared = DeclaredComponents(validator, commandType, failures);
            var exercised = new HashSet<RuleComponent>();

            // 基线：全字段合法必须零错误。没有它，下面每一格「有错」都可能是「什么都拦」。
            var baseline = Validate(validator, build(Valid));
            if (!baseline.IsValid)
            {
                failures.Add($"{commandType.Name} 合法基线被拒：{Describe(baseline)}");
            }

            foreach (var ruleCase in InvalidCases)
            {
                var result = Validate(validator, build(ruleCase.Break(Valid)));
                foreach (var error in result.Errors)
                {
                    exercised.Add(RuleComponent.FromFailure(error));
                }

                if (result.IsValid)
                {
                    failures.Add($"{commandType.Name} / {ruleCase.Name}：违例入参被判为合法。");
                    continue;
                }

                // 大小写不敏感：netcorepal 把 FluentValidation 的属性名解析成 camelCase
                // （对外报的是 organizationId 而不是 OrganizationId）。这里要钉的是「打红了哪个字段」，
                // 不是命名策略本身，所以按大小写不敏感比对——各字段之间照样两两可分。
                var properties = result.Errors.Select(error => error.PropertyName).Distinct().ToArray();
                if (!properties.Contains(ruleCase.ExpectedProperty, StringComparer.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"{commandType.Name} / {ruleCase.Name}：期望打红 {ruleCase.ExpectedProperty}，"
                        + $"实际打红 [{string.Join(", ", properties)}]。");
                }

                // 只改了一个字段，就只允许那一个字段被打红——否则「相邻同型守卫兜住变异」，
                // 删掉目标规则这一格仍然红。
                var collateral = properties
                    .Where(x => !string.Equals(x, ruleCase.ExpectedProperty, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (collateral.Length > 0)
                {
                    failures.Add(
                        $"{commandType.Name} / {ruleCase.Name}：只改了一个字段却连带打红 [{string.Join(", ", collateral)}]，"
                        + "这一格对目标规则没有鉴别力。");
                }
            }

            foreach (var boundCase in AtBoundCases)
            {
                var result = Validate(validator, build(boundCase.Break(Valid)));
                if (!result.IsValid)
                {
                    failures.Add($"{commandType.Name} / {boundCase.Name}：恰好取到上界的合法入参被拒：{Describe(result)}");
                }
            }

            // at-bound 覆盖面同样从规则树派生：每条长度规则都必须有一格「恰好取到它的上界」。
            // 没有这段，新加一条 MaximumLength 时只会被逼着补违例格（超界会红），
            // 界在哪则无人钉——而 PinnedLengthBounds 钉的是声明值，钉不到「运行时真的接受 max」。
            foreach (var (rule, isCollectionElement) in Rules(validator))
            {
                var member = NormalizeMember(rule.Member?.Name ?? rule.PropertyName);
                foreach (var component in rule.Components)
                {
                    if (component.Validator is not ILengthValidator { Max: > 0 } length)
                    {
                        continue;
                    }

                    var pinned = AtBoundCases.Any(boundCase =>
                        MemberLength(boundCase.Break(Valid), member, isCollectionElement) == length.Max);
                    if (!pinned)
                    {
                        failures.Add(
                            $"{commandType.Name}：长度规则 {new LengthBound(member, isCollectionElement, length.Max)} "
                            + "没有任何 AtBoundCases 格把它的上界恰好取到，"
                            + "「超界会红」证不到「界在哪」。");
                    }
                }
            }

            // 双向相等：声明了却没有任何违例格打红它 ⇒ 覆盖面缩水；
            // 打红了却不在声明里 ⇒ 规则树读法失配。两个方向都必须红。
            var uncovered = declared.Except(exercised).OrderBy(x => x.ToString(), StringComparer.Ordinal).ToArray();
            var unexpected = exercised.Except(declared).OrderBy(x => x.ToString(), StringComparer.Ordinal).ToArray();
            if (uncovered.Length > 0)
            {
                failures.Add(
                    $"{commandType.Name}：校验器声明了这些规则分量，却没有任何违例格把它打红"
                    + $"（{uncovered.Length}）：{string.Join(", ", uncovered.Select(x => x.ToString()))}。"
                    + "违例格名单不得少于规则树——要么补格，要么说明为什么这条规则不需要防线。");
            }

            if (unexpected.Length > 0)
            {
                failures.Add(
                    $"{commandType.Name}：违例格打红了规则树里读不到的分量"
                    + $"（{unexpected.Length}）：{string.Join(", ", unexpected.Select(x => x.ToString()))}。"
                    + "多半是规则树读法（成员名归一 / 集合规则判定 / ErrorCode）失配。");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// 这些规则确实在 MediatR 管道里跑到——不是只挂在容器里。
    /// </summary>
    /// <remarks>
    /// 逐字段鉴别力由 <see cref="Every_rule_discriminates_on_every_assignment_command"/> 承担；
    /// 这里每条命令只跑两格：一格违例（管道必须拒）、一格异字段对照
    /// （管道报出来的必须是**真正被违反的那个字段**，不是一律拒）。
    /// 对照格不落 DB：校验行为位于 handler 之前，两格都在管道内终止。
    /// </remarks>
    [Fact]
    public async Task Assignment_rules_run_inside_the_mediatr_pipeline()
    {
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        foreach (var commandType in AssignmentCommandTypes())
        {
            var build = Fixtures[commandType];

            var overLongKey = await Assert.ThrowsAsync<KnownException>(() =>
                sender.Send(build(Valid with { IdempotencyKey = new string('k', 129) })));
            Assert.Contains("128", overLongKey.Message, StringComparison.Ordinal);

            var emptyPool = await Assert.ThrowsAsync<KnownException>(() =>
                sender.Send(build(Valid with { PoolCode = "" })));
            Assert.DoesNotContain("128", emptyPool.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 校验器实际声明的长度上界集合必须与 <see cref="PinnedLengthBounds"/> **双向相等**。
    /// </summary>
    /// <remarks>
    /// 这条钉的是**数值本身**（来源是承载列宽，见 <see cref="PinnedLengthBounds"/> 的说明），
    /// 与「分量被打红了没有」是两件事：把 <c>MaximumLength(128)</c> 改成 <c>(129)</c> 时，
    /// 分量覆盖那条会因为 129 字符入参不再被拒而红，本条则直接指出 128→129 这个数变了。
    /// </remarks>
    [Fact]
    public async Task Declared_length_bounds_match_the_pinned_table()
    {
        await using var factory = CreateHost();
        using var scope = factory.Services.CreateScope();

        var expected = PinnedLengthBounds
            .Select(x => x.ToString())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        foreach (var commandType in AssignmentCommandTypes())
        {
            var validator = (IValidator)scope.ServiceProvider
                .GetRequiredService(typeof(IValidator<>).MakeGenericType(commandType));

            var actual = new List<string>();
            foreach (var (rule, isCollectionElement) in Rules(validator))
            {
                foreach (var component in rule.Components)
                {
                    if (component.Validator is ILengthValidator { Max: > 0 } length)
                    {
                        actual.Add(new LengthBound(
                            NormalizeMember(rule.Member?.Name ?? rule.PropertyName),
                            isCollectionElement,
                            length.Max).ToString());
                    }
                }
            }

            Assert.Equal(expected, actual.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>
    /// 读出某个 at-bound 夹具里目标成员的**长度**，用来核对它是否恰好取到该规则的上界。
    /// </summary>
    /// <remarks>
    /// 成员按名字从 <see cref="AssignmentInput"/> 反射取，不维护第二张手写映射表；
    /// 集合元素规则取集合里最长的那一项（本类的 at-bound 夹具都只放一项）。
    /// </remarks>
    private static int? MemberLength(AssignmentInput input, string member, bool collectionElement)
    {
        var property = typeof(AssignmentInput).GetProperty(member, BindingFlags.Instance | BindingFlags.Public);
        var value = property?.GetValue(input);
        if (collectionElement)
        {
            return value is IReadOnlyCollection<string> items && items.Count > 0
                ? items.Max(item => item?.Length ?? 0)
                : null;
        }

        return (value as string)?.Length;
    }

    /// <summary>校验器规则树里的一个分量：成员 × 是否集合元素规则 × 分量校验器名。</summary>
    private readonly record struct RuleComponent(string Member, bool CollectionElement, string Validator)
    {
        /// <summary>
        /// 从一条 <see cref="ValidationFailure"/> 反推它命中的分量。
        /// </summary>
        /// <remarks>
        /// <c>PropertyName</c> 在真实 host 里被 netcorepal 解析成 camelCase，
        /// 集合元素则带 <c>[n]</c> 下标——正是这个下标把
        /// <c>RuleFor(集合).NotEmpty()</c> 与 <c>RuleForEach(集合).NotEmpty()</c> 区分开，
        /// 否则两者会塌成同一个键、少一个位点还照绿。
        /// </remarks>
        public static RuleComponent FromFailure(ValidationFailure failure)
        {
            var name = failure.PropertyName;
            var bracket = name.IndexOf('[', StringComparison.Ordinal);
            var collectionElement = bracket >= 0;
            if (collectionElement)
            {
                name = name[..bracket];
            }

            return new RuleComponent(NormalizeMember(name), collectionElement, failure.ErrorCode);
        }

        public override string ToString() =>
            CollectionElement ? $"{Member}[].{Validator}" : $"{Member}.{Validator}";
    }

    /// <summary>一条长度上界：成员 × 是否集合元素规则 × 上界值。</summary>
    private readonly record struct LengthBound(string Member, bool CollectionElement, int Max)
    {
        public override string ToString() =>
            CollectionElement ? $"{Member}[]<={Max}" : $"{Member}<={Max}";
    }

    /// <summary>
    /// 遍历校验器建出来的规则树，同时给出「这条规则是不是 <c>RuleForEach</c> 建的集合元素规则」。
    /// </summary>
    private static IEnumerable<(IValidationRule Rule, bool IsCollectionElement)> Rules(IValidator validator)
    {
        // FluentValidation 的 AbstractValidator 自身就是规则的 IEnumerable；
        // 读规则树而不是读源码文本，所以经共享入口 Configure<TCommand> 加进来的规则同样读得到。
        var rules = Assert.IsAssignableFrom<IEnumerable<IValidationRule>>(validator);
        foreach (var rule in rules)
        {
            var isCollectionElement = rule.GetType().GetInterfaces().Any(x =>
                x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ICollectionRule<,>));
            yield return (rule, isCollectionElement);
        }
    }

    private static HashSet<RuleComponent> DeclaredComponents(
        IValidator validator,
        Type commandType,
        List<string> failures)
    {
        var declared = new HashSet<RuleComponent>();
        foreach (var (rule, isCollectionElement) in Rules(validator))
        {
            var member = NormalizeMember(rule.Member?.Name ?? rule.PropertyName);
            foreach (var component in rule.Components)
            {
                var key = new RuleComponent(member, isCollectionElement, component.Validator.Name);
                if (!declared.Add(key))
                {
                    // 同一分量出现两次会在集合里塌成一个，其中一份就再也没人要求覆盖。
                    // 不静默合并：逼当轮显式处理（拆成不同 ErrorCode，或说明为什么重复是有意的）。
                    failures.Add(
                        $"{commandType.Name}：规则树里出现重复分量 {key}，"
                        + "覆盖面集合会把它塌成一个位点，必须显式处理而不是静默合并。");
                }
            }
        }

        if (declared.Count == 0)
        {
            failures.Add($"{commandType.Name}：从规则树读到 0 个分量，覆盖面断言已退化成空断言。");
        }

        return declared;
    }

    /// <summary>
    /// 成员名归一：规则树给的是反射成员名（PascalCase），
    /// <see cref="ValidationFailure.PropertyName"/> 在真实 host 里是 camelCase，两者必须能对上。
    /// 归一只动每一段的首字母大小写，不做模糊匹配。
    /// </summary>
    private static string NormalizeMember(string member) =>
        string.Join('.', member.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToUpperInvariant(segment[0]) + segment[1..]));

    private static IReadOnlyList<Type> AssignmentCommandTypes() =>
        typeof(IWarehouseAssignmentCommand).Assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && !type.IsGenericTypeDefinition
                && typeof(IWarehouseAssignmentCommand).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    private static ValidationResult Validate(IValidator validator, object instance) =>
        validator.Validate(new ValidationContext<object>(instance));

    private static string Describe(ValidationResult result) =>
        string.Join(", ", result.Errors.Select(error => $"{error.PropertyName}:{error.ErrorMessage}"));

    private static WebApplicationFactory<Program> CreateHost() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token"));

    private sealed record AssignmentInput(
        string OrganizationId,
        string EnvironmentId,
        string AssignerPrincipalId,
        IReadOnlyCollection<string> AuthorizedSiteCodes,
        string PoolCode,
        string? OperatorPrincipalId,
        string IdempotencyKey,
        long ExpectedVersion);

    private sealed record RuleCase(string Name, string ExpectedProperty, Func<AssignmentInput, AssignmentInput> Break);
}
