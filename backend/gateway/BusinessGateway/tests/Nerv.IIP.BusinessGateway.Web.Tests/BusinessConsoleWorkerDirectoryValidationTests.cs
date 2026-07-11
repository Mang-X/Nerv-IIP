using System.Linq;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// 人员目录分页契约边界（MAN-461 / #815 / #868）。前端曾发 <c>pageIndex: 0</c>，被本 validator
/// 的 <c>PageIndex &gt; 0</c> 规则拒为 400，三处人员选择器静默空。前端单测 mock 了端点、未校验取值，
/// 门禁因此漏过。此测试从服务端钉住 1-based 契约：任何人放松 <c>GreaterThan(0)</c> 都会红。
/// 与前端 <c>useBusinessMasterData.test.ts</c>（断言发 1）两端夹住。
/// </summary>
public sealed class BusinessConsoleWorkerDirectoryValidationTests
{
    private static BusinessConsoleWorkerDirectoryRequest Request(int pageIndex) =>
        new(OrganizationId: "org-001", EnvironmentId: "env-dev", PageIndex: pageIndex);

    [Fact]
    public void PageIndex_zero_is_rejected_as_1_based_contract()
    {
        var result = new BusinessConsoleWorkerDirectoryRequestValidator().Validate(Request(0));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(BusinessConsoleWorkerDirectoryRequest.PageIndex));
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
