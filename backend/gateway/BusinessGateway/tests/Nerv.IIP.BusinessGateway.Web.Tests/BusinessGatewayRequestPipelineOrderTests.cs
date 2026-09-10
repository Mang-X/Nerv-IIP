using System.Net;
using System.Net.Http.Json;
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
/// 且该上界（128）**严格小于**全局钳 <c>BusinessGatewayIdempotencyKey.MaximumLength</c>（150）。
/// 两点都要：只有一条规则 ⇒ 400 只可能来自幂等键长度；128 &lt; 150 ⇒ 129 字符的键能穿过全局钳，
/// 于是「端点级规则是否看得见头部来源」这件事才可被检验。若哪天这条规则或全局钳被调整到
/// 两者不再满足 128 &lt; 150，本类会失去鉴别力——所以
/// <see cref="Endpoint_level_bound_is_strictly_below_the_global_clamp" /> 把这个前提也钉住。</para>
///
/// <para><b>本类不证明什么</b>：只覆盖 <see cref="AuthorizedBusinessProxyEndpoint{TRequest,TResponse}" />
/// 这一支。网关里另有几个直接继承 FastEndpoints <c>Endpoint&lt;,&gt;</c>、
/// 把鉴权写在自己 <c>HandleAsync</c> 里的端点（搜索 <c>BusinessGatewayAuthorization.Require</c> 可见），
/// 它们的次序不在本类射程内。</para>
/// </remarks>
public sealed class BusinessGatewayRequestPipelineOrderTests
{
    private const string MovementsPath = "/api/business-console/v1/inventory/movements";

    /// <summary>比端点级上界（128）长一个字符，但仍在全局钳（150）以内。</summary>
    private static readonly string OverEndpointBoundKey = new('a', 129);

    /// <summary>端点级上界上的最长合法键。</summary>
    private static readonly string AtEndpointBoundKey = new('b', 128);

    /// <summary>超过全局钳，由 <c>Resolve</c> 直接拒。</summary>
    private static readonly string OverGlobalClampKey = new('c', 151);

    [Fact]
    public void Endpoint_level_bound_is_strictly_below_the_global_clamp()
    {
        var globalClamp = Assert.Throws<BusinessServiceProxyException>(() => Normalize(new string('a', 1024)));
        Assert.Equal("idempotency-key-too-long", globalClamp.Message);

        // 129 穿得过全局钳（不抛），151 穿不过——这两条一起把 128 < 全局钳 <= 150 夹住，
        // 本类其余用例的鉴别力依赖于此。
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
    // 128 字符放行并原样送达下游，129 字符被拒且不转发——两格一起证明规则真的作用在头部来源上，
    // 而不是「凡带头部就拒」。
    [Theory]
    [InlineData(128, HttpStatusCode.OK, 1)]
    [InlineData(129, HttpStatusCode.BadRequest, 0)]
    public async Task Header_supplied_key_is_bound_by_the_endpoint_level_rule(
        int keyLength,
        HttpStatusCode expectedStatus,
        int expectedForwardCount)
    {
        var key = new string('b', keyLength);
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

        // 只断言「两边一样」还不够：把 Resolve 的异常改成逃逸，两边会一起变成同一个 500，
        // 上面那条等式照样成立。所以超过全局钳这一格必须再钉住具体形状。
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
    /// <c>ResolveForAudit</c> 是幂等键的第二条读取路径，它在 <c>ForwardAsync</c> 体内被调用，
    /// 因而永远在 <c>Resolve</c> 之后。<c>Resolve</c> 已把归一化结果写回 DTO，
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
