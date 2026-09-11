using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Inventory;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// 校验失败的公开响应形状契约（#3333）：走与本网关其它失败通道同一个
/// <c>{success,message,code,errorData}</c> 信封，<c>message</c> 位是<b>稳定错误码</b>。
/// </summary>
/// <remarks>
/// <para><b>被修的缺陷</b>：FastEndpoints 默认形状的顶层 <c>message</c> 是英文常量
/// <c>One or more errors occurred!</c>，而两端的错误展示链都从顶层 <c>message</c> 起读
/// ⇒ 操作工屏上就是那句英文常量。上屏那一段的证据在前端两端各一格
/// （<c>frontend/apps/business-console/src/utils/notify.test.ts</c> 与
/// <c>frontend/apps/business-pda/src/pages/equipment/repair.test.ts</c>），本类只管 wire。</para>
///
/// <para><b>固定用 <c>/api/business-console/v1/inventory/movements</c> 的理由</b>：
/// 它的校验器只有 <c>IdempotencyKey</c> 一条长度规则，且该上界严格小于全局钳
/// （这个严格不等式由 <see cref="BusinessGatewayRequestPipelineOrderTests"/> 钉住，
/// 本类不重复证明），于是「端点级规则拒」与「全局钳拒」两条通道可以在同一个端点上对照。
/// 长度从校验器规则读，不手抄。</para>
///
/// <para><b>本类不证明什么</b>：不证明用户看到中文（那要前端那两格），
/// 不证明字段级原因可读（<c>errorData</c> 里的句子不上屏，见
/// <see cref="BusinessGatewayValidationErrorResponse"/> 的注释）。</para>
/// </remarks>
public sealed class BusinessGatewayValidationFailureEnvelopeTests
{
    private const string MovementsPath = "/api/business-console/v1/inventory/movements";

    /// <summary>本端点 <c>IdempotencyKey</c> 的端点级上界，从建出来的规则读。</summary>
    private static int EndpointBound { get; } = ResolveEndpointBound();

    /// <summary>
    /// 稳定 wire 码的确切字面量。**这一条是跨语言契约的本仓侧锚点**：
    /// 前端 <c>frontend/packages/business-core/src/labels/stableErrorMessages.ts</c>
    /// 按这个精确值查表（不归一化、不前缀匹配），改名而不同步那张表 ⇒ 操作工屏上回落成裸码。
    /// 两侧没有生成物把它们焊在一起（既有的 <c>idempotency-key-too-long</c> 等码也是同样的约定），
    /// 所以这里把字面量写死，让改名这件事至少在本仓两侧各红一次。
    /// </summary>
    [Fact]
    public void Stable_wire_code_literal_is_pinned()
    {
        Assert.Equal("request-payload-invalid", BusinessGatewayValidationErrorResponse.StableErrorCode);
    }

    /// <summary>
    /// 端点级校验失败的响应逐键成形：信封四键齐全、<c>message</c> 是稳定码、
    /// 且**不再**出现 FastEndpoints 默认形状的 <c>statusCode</c> / <c>errors</c> 两键。
    /// </summary>
    /// <remarks>
    /// 「旧键消失」是绝对锚：只断言新键存在的话，一个把两种形状**并集**写出去的实现也会绿，
    /// 而那种实现里 PC 的 <c>serverErrorMessage</c> 仍可能读到旧字段。
    /// </remarks>
    [Fact]
    public async Task Endpoint_level_validation_failure_uses_the_stable_code_envelope()
    {
        var (status, body) = await PostAsync(MovementBody(new string('a', EndpointBound + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(
            ["success", "message", "code", "errorData"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(
            BusinessGatewayValidationErrorResponse.StableErrorCode,
            root.GetProperty("message").GetString());
        Assert.Equal(400, root.GetProperty("code").GetInt32());

        // 逐字段原因搬到了 errorData（值域与旧 errors{} 逐字相同，只换槽位），
        // 断言它非空，免得哪天「简化」成 [] 又把排障信息悄悄删掉。
        var items = root.GetProperty("errorData").EnumerateArray().ToArray();
        var item = Assert.Single(items);
        Assert.Equal("idempotencyKey", item.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("reason").GetString()));
    }

    /// <summary>
    /// 模型绑定失败（不是任何 <c>Validator&lt;&gt;</c> 产出的）也走同一条通道。
    /// </summary>
    /// <remarks>
    /// 这一格把射程从「有校验器的端点」扩到「FastEndpoints 会写校验失败响应的全部情形」：
    /// <c>quantity</c> 收到字符串，反序列化失败由 FastEndpoints 记成 ValidationFailure，
    /// 同样经 <c>Errors.ResponseBuilder</c> 成形。若哪天有人把 ResponseBuilder 换成只处理
    /// 某一类失败的实现，这一格会红。
    /// </remarks>
    [Fact]
    public async Task Model_binding_failure_uses_the_same_envelope()
    {
        var (status, body) = await PostRawAsync(
            """{"organizationId":"org-001","environmentId":"env-dev","quantity":"not-a-number"}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(
            ["success", "message", "code", "errorData"],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(
            BusinessGatewayValidationErrorResponse.StableErrorCode,
            root.GetProperty("message").GetString());
    }

    /// <summary>
    /// 同一端点的两条拒绝通道（端点级 DTO 规则 / 全局钳）现在是**同一个形状**，
    /// 差别只剩 <c>message</c> 位上的稳定码。
    /// </summary>
    /// <remarks>
    /// <para>#3333 票面记的「同端点两种形状」在本票开工时**仍然存在**：#3330 把归一化前移到 DTO 校验之前，
    /// 消掉的是「同一个错误按**键来源**（头部 / 请求体）分成两种形状」；实测两条来源在各自 regime 下
    /// 逐字节相同（见 <see cref="BusinessGatewayRequestPipelineOrderTests.Header_and_body_sources_are_rejected_through_the_same_channel"/>）。
    /// 留下的分叉是按**哪条上界**：端点级规则由 DTO 校验答（旧：FastEndpoints 默认形状），
    /// 全局钳由 <c>BusinessGatewayIdempotencyKey.Resolve</c> 答（ResponseData 信封）。本条收敛的是后者。</para>
    /// <para>断言「键集合相同」而不是「响应体相同」：两条通道**应当**给不同的稳定码
    /// （<c>request-payload-invalid</c> vs <c>idempotency-key-too-long</c>），
    /// 断言逐字相同会把这个有意的差别也钉死。同时两条都断言了绝对值，
    /// 不存在「一起劣化成同一种坏形状仍然绿」的漏洞。</para>
    /// </remarks>
    [Fact]
    public async Task Both_rejection_channels_now_share_one_shape()
    {
        var (endpointStatus, endpointBody) = await PostAsync(MovementBody(new string('a', EndpointBound + 1)));
        var (clampStatus, clampBody) = await PostAsync(
            MovementBody(new string('c', BusinessGatewayIdempotencyKey.MaximumKeyLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, endpointStatus);
        Assert.Equal(HttpStatusCode.BadRequest, clampStatus);
        using var endpoint = JsonDocument.Parse(endpointBody);
        using var clamp = JsonDocument.Parse(clampBody);
        Assert.Equal(Keys(clamp), Keys(endpoint));
        Assert.Equal(
            BusinessGatewayValidationErrorResponse.StableErrorCode,
            endpoint.RootElement.GetProperty("message").GetString());
        Assert.Equal("idempotency-key-too-long", clamp.RootElement.GetProperty("message").GetString());

        static string[] Keys(JsonDocument document) =>
            document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(object body)
    {
        using var lease = await LeaseAsync();
        var response = await lease.Client.PostAsJsonAsync(MovementsPath, body);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostRawAsync(string json)
    {
        using var lease = await LeaseAsync();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await lease.Client.PostAsync(MovementsPath, content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<LeasedClient> LeaseAsync()
    {
        var inventory = new RecordingInventoryClient();
        var lease = BusinessGatewayTestHost.Lease(
            FakeBusinessGatewayAuthorizationClient.AllowOnly(
                BusinessGatewayPermissions.InventoryMovementsCreate),
            services =>
            {
                services.RemoveAll<IBusinessInventoryClient>();
                services.AddSingleton<IBusinessInventoryClient>(inventory);
            },
            BusinessGatewayTestHostProfile.ServiceBaseUrls);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        await Task.CompletedTask;
        return new LeasedClient(lease, client, inventory);
    }

    private sealed record LeasedClient(
        BusinessGatewayTestHostLease Lease,
        HttpClient Client,
        RecordingInventoryClient Inventory) : IDisposable
    {
        public void Dispose()
        {
            // 转发计数是绝对锚：任何一格都不得在被拒之后还把请求送到下游。
            Assert.Equal(0, Inventory.MovementCallCount);
            Lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

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

        Assert.True(maximum > 0, "解析不到 IdempotencyKey 的端点级长度上界，本类夹具已失去意义。");
        return maximum;
    }

    private static object MovementBody(string? idempotencyKey) => new
    {
        organizationId = "org-001",
        environmentId = "env-dev",
        movementType = "outbound",
        sourceService = "business-console",
        sourceDocumentId = "OUT-VALIDATION-ENVELOPE",
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
}
