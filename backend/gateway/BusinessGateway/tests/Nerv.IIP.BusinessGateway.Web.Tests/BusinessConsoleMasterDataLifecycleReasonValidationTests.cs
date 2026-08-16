using System.Linq;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// 主数据停用 / 重新启用的原因契约（#878）。MasterData 的
/// <c>SetMasterDataResourceEnabledCommandHandler</c> 对空原因稳定拒绝、并把原因写进生命周期审计；
/// 网关此前只限长度不限非空，空原因要走完一次代理转发才在下游被拒。本测试把网关口径钉成与下游一致：
/// 非空 + 同一 500 长度上限。
///
/// 只断言 <c>IsValid</c>，不断言 <c>PropertyName</c> / 错误文案——后者受 FastEndpoints /
/// FluentValidation 全局解析器与本地化影响，且会被同程序集内其它测试改写，断言不稳定
/// （同 <see cref="BusinessConsoleWorkerDirectoryValidationTests"/> 的取舍）。改用「差分」证明规则：
/// 其余字段保持合法，只翻转 Reason —— 有内容合法、空 / 空白 / 超长非法，则唯一致失败的就是 Reason 规则。
/// </summary>
public sealed class BusinessConsoleMasterDataLifecycleReasonValidationTests
{
    private static BusinessConsoleSetMasterDataResourceEnabledRequest Request(string reason) =>
        new(
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            ResourceType: "sku",
            Code: "SKU-001",
            IdempotencyKey: "idem-master-data-lifecycle-reason",
            Reason: reason);

    [Fact]
    public void Reason_carrying_real_text_keeps_the_request_valid()
    {
        var result = new BusinessConsoleSetMasterDataResourceEnabledRequestValidator()
            .Validate(Request("产线拆除，改用公制单位"));

        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(failure => failure.ErrorMessage)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("　")] // 全角空格：中文输入法下最容易被当成"填了"的空原因。
    public void Blank_reason_makes_an_otherwise_valid_request_invalid(string reason)
    {
        var validator = new BusinessConsoleSetMasterDataResourceEnabledRequestValidator();

        // 差分：基线（有内容）合法，仅把 Reason 翻成空/空白即失败 → 隔离出 Reason 非空规则。
        Assert.True(validator.Validate(Request("设备报废")).IsValid);
        Assert.False(validator.Validate(Request(reason)).IsValid);
    }

    [Fact]
    public void Reason_length_ceiling_matches_the_master_data_handler()
    {
        var validator = new BusinessConsoleSetMasterDataResourceEnabledRequestValidator();

        // 下游 handler 的上限是 500：网关放行 500、拒绝 501，两侧不留"网关放过、下游再拒"的缝。
        Assert.True(validator.Validate(Request(new string('停', 500))).IsValid);
        Assert.False(validator.Validate(Request(new string('停', 501))).IsValid);
    }

    /// <summary>
    /// 请求合同的默认原因是空串——也就是"调用方不传 Reason"时的真实取值。
    /// 默认值必须落在拒绝侧，否则前端漏传原因会被静默放行成一条无理由的审计事实。
    /// </summary>
    [Fact]
    public void Omitting_reason_falls_on_the_rejected_side()
    {
        var omitted = new BusinessConsoleSetMasterDataResourceEnabledRequest(
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            ResourceType: "sku",
            Code: "SKU-001",
            IdempotencyKey: "idem-master-data-lifecycle-reason");

        Assert.False(new BusinessConsoleSetMasterDataResourceEnabledRequestValidator().Validate(omitted).IsValid);
    }
}
