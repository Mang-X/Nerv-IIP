using System.Linq;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// 人员目录分页契约边界（MAN-461 / #815 / #868）。前端曾发 <c>pageIndex: 0</c>，被本 validator
/// 的 <c>PageIndex &gt; 0</c> 规则拒为 400，三处人员选择器静默空。前端单测 mock 了端点、未校验取值，
/// 门禁因此漏过。此测试从服务端钉住 1-based 契约：任何人放松 <c>GreaterThan(0)</c> 都会红。
/// 与前端 <c>useBusinessMasterData.test.ts</c>（断言发 1）两端夹住。
///
/// 只断言 <c>IsValid</c>，不断言 <c>PropertyName</c> / 展示名 / 错误文案——后者受 FastEndpoints /
/// FluentValidation 全局解析器影响（应用把 JSON/属性名解析为 camelCase，见 Program.cs），且会被同
/// 程序集内其它测试改写，隔离运行为 "PageIndex"、完整测试集里为 "pageIndex"，导致断言不稳定
/// （PR #867 CI: 1 failed/474）。改用「差分」证明规则：保持其余字段合法，只翻转 PageIndex —
/// 1 合法、0/-1 非法，则唯一致失败的就是 <c>PageIndex &gt; 0</c> 规则。
/// </summary>
public sealed class BusinessConsoleWorkerDirectoryValidationTests
{
    private static BusinessConsoleWorkerDirectoryRequest Request(int pageIndex) =>
        new(OrganizationId: "org-001", EnvironmentId: "env-dev", PageIndex: pageIndex);

    [Fact]
    public void PageIndex_below_one_makes_an_otherwise_valid_request_invalid()
    {
        var validator = new BusinessConsoleWorkerDirectoryRequestValidator();

        // 差分：其余字段保持合法，基线 PageIndex=1 通过；仅把 PageIndex 翻到 0/-1 即失败 →
        // 隔离出 PageIndex > 0 规则，不依赖会被全局解析器改写的属性名 / 展示名。
        Assert.True(validator.Validate(Request(1)).IsValid);
        Assert.False(validator.Validate(Request(0)).IsValid);
        Assert.False(validator.Validate(Request(-1)).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    public void PageIndex_one_or_greater_is_accepted(int pageIndex)
    {
        var result = new BusinessConsoleWorkerDirectoryRequestValidator().Validate(Request(pageIndex));

        Assert.True(
            result.IsValid,
            string.Join("; ", result.Errors.Select(failure => failure.ErrorMessage)));
    }
}
