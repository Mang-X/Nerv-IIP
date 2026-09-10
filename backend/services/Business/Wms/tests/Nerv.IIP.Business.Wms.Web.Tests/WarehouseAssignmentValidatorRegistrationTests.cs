using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
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
/// <para><b>值域怎么来</b>：命令集合由**反射** Wms.Web 程序集里
/// <see cref="IWarehouseAssignmentCommand"/> 的全部具体实现得到，不是手写名单。
/// <see cref="Fixtures"/> 只提供各命令的构造夹具，其键集必须与反射集合逐一相等
/// （<see cref="Every_assignment_command_has_a_fixture"/>），
/// 新增第 6 条分配命令而不给它校验器或夹具都会红。</para>
///
/// <para><b>本类不证明什么</b>：不证明网关侧上界与这里一致（那是
/// <c>BusinessGatewayIdempotencyKeyDownstreamBoundContractTests</c> 的射程）；
/// 不证明这些上界与落库列宽一致（分配 receipt 表存的是 <c>WmsText.IdempotencyKey</c>
/// 的 SHA256 派生值，列宽对原始键零约束）；
/// 端到端那条只对每条命令各跑一格违例 + 一格异字段对照，
/// 逐字段的鉴别力由 <see cref="Every_rule_discriminates_on_every_assignment_command"/> 承担。</para>
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
        var checkedCells = 0;
        foreach (var commandType in AssignmentCommandTypes())
        {
            var validator = (IValidator)scope.ServiceProvider
                .GetRequiredService(typeof(IValidator<>).MakeGenericType(commandType));
            var build = Fixtures[commandType];

            // 基线：全字段合法必须零错误。没有它，下面每一格「有错」都可能是「什么都拦」。
            var baseline = Validate(validator, build(Valid));
            if (!baseline.IsValid)
            {
                failures.Add($"{commandType.Name} 合法基线被拒：{Describe(baseline)}");
            }

            foreach (var ruleCase in InvalidCases)
            {
                checkedCells++;
                var result = Validate(validator, build(ruleCase.Break(Valid)));
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
                checkedCells++;
                var result = Validate(validator, build(boundCase.Break(Valid)));
                if (!result.IsValid)
                {
                    failures.Add($"{commandType.Name} / {boundCase.Name}：恰好取到上界的合法入参被拒：{Describe(result)}");
                }
            }
        }

        Assert.Equal((InvalidCases.Length + AtBoundCases.Length) * Fixtures.Count, checkedCells);
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
