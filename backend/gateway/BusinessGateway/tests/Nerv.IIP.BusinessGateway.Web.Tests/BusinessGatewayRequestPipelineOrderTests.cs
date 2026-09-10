using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Inventory;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// 代理端点的请求处理次序契约（#3330）：<b>鉴权 → 幂等键归一化 → DTO 校验 → 转发</b>。
/// </summary>
/// <remarks>
/// <para>此前 <c>AuthorizedBusinessProxyEndpoint</c> 把鉴权与
/// <see cref="BusinessGatewayIdempotencyKey.Resolve{TRequest}" /> 都写在 <c>HandleAsync</c> 体内，
/// 而 FastEndpoints 的 DTO 校验跑在 <c>HandleAsync</c> 之前。同一个次序错位有两个后果，本类各钉一面：</para>
/// <list type="number">
/// <item><description>校验发生时 <c>IdempotencyKey</c> 还是 <c>null</c>，
/// 于是经 <c>Idempotency-Key</c> 头传来的键对端点级规则**恒过**。</description></item>
/// <item><description>任一端点级规则命中都会先于鉴权返回 400，
/// 已认证但无权限的调用方因此收到「改对载荷就能过」的误导。</description></item>
/// </list>
///
/// <para><b>为什么固定用 <c>/api/business-console/v1/inventory/movements</c></b>：
/// 它的校验器 <c>BusinessConsolePostStockMovementRequestValidator</c> 只有
/// <c>RuleFor(x =&gt; x.IdempotencyKey).MaximumLength(128)</c> 一条规则，
/// 且该上界**严格小于**全局钳 <c>BusinessGatewayIdempotencyKey.MaximumLength</c>
/// （不写两者的具体数：本类已改成从各自权威派生，见下）。
/// 两点都要：只有一条规则 ⇒ 400 只可能来自幂等键长度；**端点级上界严格小于全局钳**
/// ⇒「端点级上界 + 1」的键能穿过全局钳，于是「端点级规则是否看得见头部来源」这件事才可被检验。
/// 若哪天这条规则或全局钳被调整到两者不再满足该严格不等式，本类会失去鉴别力——所以
/// <see cref="Endpoint_level_bound_is_strictly_below_the_global_clamp" /> 把这个前提也钉住。
/// <para>本类的所有长度夹具都**从这两个上界派生**（<see cref="EndpointBound" /> 从校验器规则读、
/// 全局钳从 <c>BusinessGatewayIdempotencyKey.MaximumKeyLength</c> 读），一个手抄数字都不留：
/// #3327 把钳从 150 抬到 512 时，此处原先手抄的 129 / 150 就已经与事实脱节。</para></para>
///
/// <para><b>本类不证明什么</b>：只覆盖 <see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}" />
/// 这一支。网关里另有几个直接继承 FastEndpoints <c>Endpoint&lt;,&gt;</c>、
/// 把鉴权写在自己 <c>HandleAsync</c> 里的端点（搜索 <c>BusinessGatewayAuthorization.Require</c> 可见），
/// 它们的次序不在本类射程内。</para>
/// </remarks>
public sealed class BusinessGatewayRequestPipelineOrderTests
{
    private const string MovementsPath = "/api/business-console/v1/inventory/movements";

    /// <summary>
    /// 本端点的端点级 <c>IdempotencyKey</c> 上界，**从校验器建出来的规则读**，不手抄。
    /// </summary>
    /// <remarks>
    /// 读规则而不是读源码文本：这条规则若被改值、改成经扩展方法加、或被挪走，本属性跟着变或解析不到，
    /// 而不是留下一个与实现脱节的常数。解析不到时 <see cref="Endpoint_level_bound_is_strictly_below_the_global_clamp" />
    /// 会因为 <c>0 &lt; 钳</c> 之外的断言先红，不会静默退化。
    /// </remarks>
    private static int EndpointBound { get; } = ResolveEndpointBound();

    /// <summary>比端点级上界长一个字符，但仍在全局钳以内（两个数都派生，见类注释）。</summary>
    private static readonly string OverEndpointBoundKey = new('a', EndpointBound + 1);

    /// <summary>端点级上界上的最长合法键。</summary>
    private static readonly string AtEndpointBoundKey = new('b', EndpointBound);

    /// <summary>
    /// 超过全局钳，由 <c>Resolve</c> 直接拒。长度按钳**派生**而不是手抄：
    /// #3327 把钳从 150 抬到 512 后，手抄的 151 不再越界，本类那几格会静默失去鉴别力。
    /// </summary>
    private static readonly string OverGlobalClampKey =
        new('c', BusinessGatewayIdempotencyKey.MaximumKeyLength + 1);

    [Fact]
    public void Endpoint_level_bound_is_strictly_below_the_global_clamp()
    {
        var globalClamp = Assert.Throws<BusinessServiceProxyException>(
            () => Normalize(new string('a', BusinessGatewayIdempotencyKey.MaximumKeyLength + 1)));
        Assert.Equal("idempotency-key-too-long", globalClamp.Message);

        // 端点级上界必须解析得到，否则 EndpointBound 会退化成 0，下面几条夹具全部变成空串。
        Assert.True(
            EndpointBound > 0,
            "BusinessConsolePostStockMovementRequestValidator 上解析不到 IdempotencyKey 的长度上界，本类夹具已失去意义。");
        Assert.True(
            EndpointBound < BusinessGatewayIdempotencyKey.MaximumKeyLength,
            $"端点级上界 {EndpointBound} 不再严格小于全局钳 {BusinessGatewayIdempotencyKey.MaximumKeyLength}，本类将失去鉴别力。");

        // 「端点级上界 + 1」穿得过全局钳（不抛），「钳 + 1」穿不过——这两条一起把上面那个严格不等式
        // 在运行时也走一遍，本类其余用例的鉴别力依赖于此。
        Assert.Equal(OverEndpointBoundKey, Normalize(OverEndpointBoundKey));
        Assert.Throws<BusinessServiceProxyException>(() => Normalize(OverGlobalClampKey));
        Assert.False(new BusinessConsolePostStockMovementRequestValidator()
            .Validate(Movement(OverEndpointBoundKey)).IsValid);
        Assert.True(new BusinessConsolePostStockMovementRequestValidator()
            .Validate(Movement(AtEndpointBoundKey)).IsValid);
    }

    // 后果 (b)：已认证但无权限的调用方，响应码不因载荷合法性而变化。
    // 三个载荷分别是「合法」「违反端点级规则」「超过全局钳」，覆盖 DTO 校验与 Resolve 两条
    // 会抢在鉴权之前作答的通道。
    [Theory]
    [InlineData("ordinary-movement-key")]
    [InlineData("over-endpoint-bound")]
    [InlineData("over-global-clamp")]
    public async Task Denied_caller_gets_forbidden_regardless_of_payload_validity(string keyKind)
    {
        var key = keyKind switch
        {
            "over-endpoint-bound" => OverEndpointBoundKey,
            "over-global-clamp" => OverGlobalClampKey,
            _ => "ordinary-movement-key",
        };
        var inventory = new RecordingInventoryClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var lease = LeaseHost(auth, inventory);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(MovementsPath, MovementBody(key));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, inventory.MovementCallCount);
    }

    // 同上，但键走 Idempotency-Key 头：两条来源都不得把 403 换成 400。
    [Theory]
    [InlineData("ordinary-movement-key")]
    [InlineData("over-endpoint-bound")]
    [InlineData("over-global-clamp")]
    public async Task Denied_caller_gets_forbidden_regardless_of_header_key_validity(string keyKind)
    {
        var key = keyKind switch
        {
            "over-endpoint-bound" => OverEndpointBoundKey,
            "over-global-clamp" => OverGlobalClampKey,
            _ => "ordinary-movement-key",
        };
        var inventory = new RecordingInventoryClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var lease = LeaseHost(auth, inventory);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var response = await client.PostAsJsonAsync(MovementsPath, MovementBody(null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, inventory.MovementCallCount);
    }

    // 后果 (a)：经 Idempotency-Key 头传来的键必须受端点级规则约束。
    // 「端点级上界」那一格放行并原样送达下游，「+1」那一格被拒且不转发——两格一起证明规则真的
    // 作用在头部来源上，而不是「凡带头部就拒」。长度按端点级上界派生，见类注释。
    [Theory]
    [InlineData(0, HttpStatusCode.OK, 1)]
    [InlineData(1, HttpStatusCode.BadRequest, 0)]
    public async Task Header_supplied_key_is_bound_by_the_endpoint_level_rule(
        int lengthOverEndpointBound,
        HttpStatusCode expectedStatus,
        int expectedForwardCount)
    {
        var key = new string('b', EndpointBound + lengthOverEndpointBound);
        var inventory = new RecordingInventoryClient();
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.InventoryMovementsCreate);
        await using var lease = LeaseHost(auth, inventory);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var response = await client.PostAsJsonAsync(MovementsPath, MovementBody(null));

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedForwardCount, inventory.MovementCallCount);
        if (expectedForwardCount > 0)
        {
            Assert.Equal(key, inventory.LastMovementRequest!.IdempotencyKey);
        }
    }

    // 「两条来源同一个错误形状」：同一个被拒的键，走头部与走请求体必须得到逐字相同的响应。
    // 两个 regime 都要覆盖——超过端点级上界（由 DTO 校验拒）与超过全局钳（由 Resolve 拒），
    // 否则只证明了其中一条通道收敛。
    [Theory]
    [InlineData("over-endpoint-bound")]
    [InlineData("over-global-clamp")]
    public async Task Header_and_body_sources_are_rejected_through_the_same_channel(string keyKind)
    {
        var key = keyKind == "over-global-clamp" ? OverGlobalClampKey : OverEndpointBoundKey;
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.InventoryMovementsCreate);

        var headerInventory = new RecordingInventoryClient();
        await using var headerLease = LeaseHost(auth, headerInventory);
        var headerClient = headerLease.CreateClient();
        BusinessGatewayTestHost.Authenticated(headerClient);
        headerClient.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var headerResponse = await headerClient.PostAsJsonAsync(MovementsPath, MovementBody(null));
        var headerBody = await headerResponse.Content.ReadAsStringAsync();

        var bodyInventory = new RecordingInventoryClient();
        await using var bodyLease = LeaseHost(auth, bodyInventory);
        var bodyClient = bodyLease.CreateClient();
        BusinessGatewayTestHost.Authenticated(bodyClient);
        var bodyResponse = await bodyClient.PostAsJsonAsync(MovementsPath, MovementBody(key));
        var bodyBody = await bodyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, headerResponse.StatusCode);
        Assert.Equal(headerResponse.StatusCode, bodyResponse.StatusCode);
        Assert.Equal(bodyBody, headerBody);
        Assert.Equal(0, headerInventory.MovementCallCount);
        Assert.Equal(0, bodyInventory.MovementCallCount);

        // 上面第一条是**绝对锚**（不是「两边一样」这种相对断言），所以两条来源一起劣化成 500
        // 会直接红——本用例不存在「两边一起坏掉还全绿」的漏洞。本 PR 里另一条两路径等价断言
        // （Header_supplied_key_is_bound_by_the_endpoint_level_rule）同样被绝对状态码锚住。
        //
        // 但 over-endpoint-bound 这一格在「Resolve 的异常改成逃逸」那格变异下**不会红**，
        // 原因是**该变异对它不可达**，不是断言弱：over-endpoint-bound 那把键仍在全局钳以内，
        // BusinessGatewayIdempotencyKey.Normalize 根本不抛，执行流进不了被变异的 catch。
        // （本仓判例：变异存活分「覆盖缺口」与「分支不可达」两种成因，这里是后者。）
        // 下面这条 Assert.Contains 钉的是稳定 wire 码本身，与那格变异无关，别当冗余删掉。
        if (keyKind == "over-global-clamp")
        {
            Assert.Contains("idempotency-key-too-long", headerBody, StringComparison.Ordinal);
        }
    }

    // Resolve 的另一种抛法（双键不一致 409）同样必须落在代理错误通道里，不能变成未处理异常。
    [Fact]
    public async Task Conflicting_key_sources_are_answered_through_the_proxy_error_channel()
    {
        var inventory = new RecordingInventoryClient();
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(
            BusinessGatewayPermissions.InventoryMovementsCreate);
        await using var lease = LeaseHost(auth, inventory);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "header-intent-001");

        var response = await client.PostAsJsonAsync(MovementsPath, MovementBody("body-intent-002"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            "idempotency-key-mismatch",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Equal(0, inventory.MovementCallCount);
    }

    /// <summary>
    /// 审计路径（<see cref="BusinessGatewayIdempotencyKey.ResolveForAudit" />）不可能与
    /// 校验过的 DTO 取值分叉。
    /// </summary>
    /// <remarks>
    /// <c>ResolveForAudit</c> 是幂等键的第二条读取路径。它在生产代码里**只有一个调用点**
    /// （<c>AuthorizedBusinessProxyEndpoint.CreateAuditContext</c>），经
    /// <c>RequireAuditContext</c> / <c>RequireIdempotentAuditContext</c> 由各端点的
    /// <c>ForwardAsync</c> 覆写体触达，因而永远在 <c>Resolve</c> 之后。<c>Resolve</c> 已把归一化结果写回 DTO，
    /// 于是 <c>ResolveForAudit</c> 看到的三个来源（标准头 / legacy 头 / 请求体）此时必然一致：
    /// 它既不会再抛 409，返回值也恒等于那个受端点级规则约束过的值。
    /// </remarks>
    [Theory]
    [InlineData("header-only", null)]
    [InlineData(null, "body-only")]
    [InlineData("agreed-key", "agreed-key")]
    [InlineData(null, null)]
    public void Audit_path_returns_exactly_the_value_resolve_wrote_into_the_dto(string? header, string? body)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers["Idempotency-Key"] = header;
        }

        var request = new RequestWithIdempotencyKey(body);
        var resolved = BusinessGatewayIdempotencyKey.Resolve(context, request);

        Assert.Equal(resolved.IdempotencyKey, BusinessGatewayIdempotencyKey.ResolveForAudit(context, resolved));
    }

    /// <summary>
    /// **鉴权推迟的那条支路上，端点级规则仍然看得见头部来的键**（#3345 阻断 1 的验收）。
    /// </summary>
    /// <remarks>
    /// <para><b>它挡的是什么</b>：<see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}" />
    /// 的 <c>TryBuildRequirements</c> 作不出鉴权结论时会推迟到 <c>HandleAsync</c>。
    /// #3330 把**鉴权与归一化一起**推迟，于是这条支路上头部来的键在 DTO 校验时仍是 <c>null</c>、
    /// 端点级规则恒过，键一路进 <c>ForwardAsync</c>。#3345 把归一化从那个早返回里拆了出来。</para>
    ///
    /// <para><b>五格把前提夹死，缺一格结论就不成立</b>（`scope` = 请求体的
    /// <c>organizationId</c>/<c>environmentId</c>）：</para>
    /// <list type="bullet">
    /// <item><c>A</c>（正常作用域 + 有声明令牌 + 头部键）：普通路径，本来就 400 —— 对照，证明本条测的不是普通路径。</item>
    /// <item><c>B-null</c>（作用域**字段缺省** + 无声明令牌 + 头部键）：**这就是那个洞**。
    /// 两边都是 <c>null</c> ⇒ <c>BusinessGatewayAuthorization</c> 的作用域相等门
    /// <c>string.Equals(null, null)</c> 为真 ⇒ 推迟的鉴权也放行。修复前实测 200 + <c>forwarded=1</c> + 键长 300。</item>
    /// <item><c>B-empty</c>（作用域**空串**）：<c>string.Equals("", null)</c> 为假 ⇒ 修复前是 403。
    /// 与 <c>B-null</c> 成对，说明触发条件是**缺省**不是**空**——审核给的「空作用域」这个说法不够精确，此处按实测写。</item>
    /// <item><c>C-null</c>（作用域缺省 + **有声明**令牌）：修复前 403（拿 <c>null</c> 跟真声明比出来的），修复后 400。</item>
    /// <item><c>D</c>（正常作用域 + 无声明令牌）：作得出鉴权结论 ⇒ **必须仍是 403**。
    /// 这一格是 #3330「鉴权先行」那条契约的护栏：本条修复不得把它变成 400。</item>
    /// </list>
    ///
    /// <para><b>键长取「端点级上界 + 1」而不是「钳 + 1」</b>：必须落在两者之间，
    /// 才能证明拒它的是**端点级规则**而不是全局钳——用「钳 + 1」时全局钳会先答，
    /// 这条支路是否看得见头部键就仍然没被检验。</para>
    ///
    /// <para><b>本条不证明什么</b>：只走 <c>/inventory/movements</c> 一个端点。
    /// 「其它端点是否也如此」由它们共用同一个基类保证，不由本条枚举。</para>
    /// </remarks>
    [Theory]
    [InlineData("A", "scoped", true, HttpStatusCode.BadRequest)]
    [InlineData("B-null", "absent", false, HttpStatusCode.BadRequest)]
    [InlineData("B-empty", "empty", false, HttpStatusCode.BadRequest)]
    [InlineData("C-null", "absent", true, HttpStatusCode.BadRequest)]
    [InlineData("D", "scoped", false, HttpStatusCode.Forbidden)]
    public async Task Deferred_authorization_still_binds_header_supplied_keys_to_the_endpoint_level_rule(
        string probe,
        string scopeKind,
        bool tokenCarriesScopeClaims,
        HttpStatusCode expectedStatus)
    {
        var inventory = new RecordingInventoryClient();
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var lease = LeaseHost(auth, inventory);
        var client = lease.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            BusinessGatewayTestTokens.ValidAccessToken(
                includeOrganizationId: tokenCarriesScopeClaims,
                includeEnvironmentId: tokenCarriesScopeClaims));
        client.DefaultRequestHeaders.Add("Idempotency-Key", OverEndpointBoundKey);

        var response = await client.PostAsJsonAsync(MovementsPath, ScopedMovementBody(scopeKind));

        Assert.Equal(expectedStatus, response.StatusCode);

        // 绝对锚：无论哪一格，超出端点级上界的键都**不得**被转发下去。
        // 只断言状态码会漏掉「400 但仍然转发」这种形状。
        Assert.Equal(0, inventory.MovementCallCount);
        Assert.Null(inventory.LastMovementRequest);
        Assert.False(string.IsNullOrEmpty(probe));
    }

    private static object ScopedMovementBody(string scopeKind) => new
    {
        organizationId = scopeKind switch { "scoped" => "org-001", "empty" => "", _ => (string?)null },
        environmentId = scopeKind switch { "scoped" => "env-dev", "empty" => "", _ => (string?)null },
        movementType = "outbound",
        sourceService = "business-console",
        sourceDocumentId = "OUT-PIPELINE-DEFERRED",
        sourceDocumentLineId = "LINE-001",
        idempotencyKey = (string?)null,
        skuCode = "SKU-001",
        uomCode = "EA",
        siteCode = "S1",
        locationCode = "L1",
        lotNo = "LOT-001",
        serialNo = (string?)null,
        qualityStatus = "qualified",
        ownerType = "company",
        ownerId = "owner-001",
        quantity = -1m,
        allowExpiredStock = false,
    };

    /// <summary>从 <c>BusinessConsolePostStockMovementRequestValidator</c> 建出来的规则里读
    /// <c>IdempotencyKey</c> 的长度上界。读规则不读源码文本。</summary>
    private static int ResolveEndpointBound()
    {
        var maximum = 0;
        foreach (var rule in (IEnumerable<IValidationRule>)new BusinessConsolePostStockMovementRequestValidator())
        {
            if (!string.Equals(rule.Member?.Name ?? rule.PropertyName, "IdempotencyKey", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var component in rule.Components)
            {
                if (component.Validator is ILengthValidator length && length.Max > 0)
                {
                    maximum = maximum == 0 ? length.Max : Math.Min(maximum, length.Max);
                }
            }
        }

        return maximum;
    }

    private static string? Normalize(string key)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = key;
        return BusinessGatewayIdempotencyKey.Resolve(context, new RequestWithIdempotencyKey(null)).IdempotencyKey;
    }

    private static BusinessConsolePostStockMovementRequest Movement(string key) =>
        new(
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            MovementType: "outbound",
            SourceService: "business-console",
            SourceDocumentId: "OUT-PIPELINE-ORDER",
            SourceDocumentLineId: "LINE-001",
            IdempotencyKey: key,
            SkuCode: "SKU-001",
            UomCode: "EA",
            SiteCode: "S1",
            LocationCode: "L1",
            LotNo: "LOT-001",
            SerialNo: null,
            QualityStatus: "qualified",
            OwnerType: "company",
            OwnerId: "owner-001",
            Quantity: -1m);

    private static object MovementBody(string? idempotencyKey) => new
    {
        organizationId = "org-001",
        environmentId = "env-dev",
        movementType = "outbound",
        sourceService = "business-console",
        sourceDocumentId = "OUT-PIPELINE-ORDER",
        sourceDocumentLineId = "LINE-001",
        idempotencyKey,
        skuCode = "SKU-001",
        uomCode = "EA",
        siteCode = "S1",
        locationCode = "L1",
        lotNo = "LOT-001",
        serialNo = (string?)null,
        qualityStatus = "qualified",
        ownerType = "company",
        ownerId = "owner-001",
        quantity = -1m,
        allowExpiredStock = false,
    };

    private static BusinessGatewayTestHostLease LeaseHost(
        FakeBusinessGatewayAuthorizationClient auth,
        RecordingInventoryClient inventory) =>
        BusinessGatewayTestHost.Lease(
            auth,
            services =>
            {
                services.RemoveAll<IBusinessInventoryClient>();
                services.AddSingleton<IBusinessInventoryClient>(inventory);
            },
            BusinessGatewayTestHostProfile.ServiceBaseUrls);

    /// <summary>本类自己的最小 DTO：只需要一个 <c>public string IdempotencyKey</c>，
    /// 因为 <c>Resolve</c> 的生效判据就是这个属性的存在与类型。</summary>
    private sealed record RequestWithIdempotencyKey(string? IdempotencyKey);
}
