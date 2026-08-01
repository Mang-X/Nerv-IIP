using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// #1397 / 第三轮走查台账 #81：出库复核必 422，但响应体恒为 <c>{"message":"unprocessable"}</c>，
/// 拒绝理由只进服务端日志——用户无从自助定位。这些用例锁住「理由必须出得来」。
/// </summary>
public sealed class WmsUnprocessableReasonCodeTests
{
    [Fact]
    public async Task Unprocessable_response_carries_the_reason_code_instead_of_the_flat_constant()
    {
        var response = await InvokeAsync(
            new WmsUnprocessableException(
                "Outbound order requires terminal picking task execution facts.",
                WmsUnprocessableReasonCodes.OutboundPickingNotCompleted));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            WmsUnprocessableReasonCodes.OutboundPickingNotCompleted,
            response.Body!.Message);
        // 这就是被修掉的行为：所有 422 都长成同一句 "unprocessable"。
        Assert.NotEqual(WmsUnprocessableException.SafeCode, response.Body.Message);
    }

    [Fact]
    public async Task Unprocessable_without_a_reason_code_still_falls_back_to_the_safe_constant()
    {
        var response = await InvokeAsync(new WmsUnprocessableException("some unclassified reason"));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        Assert.Equal(WmsUnprocessableException.SafeCode, response.Body!.Message);
    }

    [Fact]
    public async Task Authorization_denial_reports_which_guard_rejected_it()
    {
        var response = await InvokeAsync(
            WmsAuthorizationException.Forbidden("resource-not-assigned-to-self"));

        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
        // 「派给别人了」与「不在你的作业范围」过去在界面上长得一模一样（台账 #82）。
        Assert.Equal("resource-not-assigned-to-self", response.Body!.Message);
    }

    /// <summary>
    /// 外发代码必须过网关的下游消息护栏（仅 ASCII 字母数字与 <c>- _ .</c>），
    /// 否则网关会把它换成 <c>downstream-request-failed</c>——那比现在更难排查。
    /// </summary>
    [Theory]
    [InlineData(WmsUnprocessableReasonCodes.OutboundPackReviewNotPassed)]
    [InlineData(WmsUnprocessableReasonCodes.OutboundPickingTaskMissing)]
    [InlineData(WmsUnprocessableReasonCodes.OutboundPickingNotCompleted)]
    [InlineData(WmsUnprocessableReasonCodes.OutboundPickingDifferenceReasonMissing)]
    [InlineData(WmsUnprocessableReasonCodes.OutboundLinePickingTaskMissing)]
    [InlineData(WmsUnprocessableReasonCodes.PickingDifferenceReasonRequired)]
    [InlineData(WmsUnprocessableReasonCodes.ExecutedQuantityOutOfRange)]
    public void Every_reason_code_survives_the_gateway_safe_message_filter(string code)
    {
        Assert.Equal(code, WmsLifecycleConflictMiddleware.SafeOutboundCode(code, "fallback"));
    }

    [Theory]
    [InlineData("含中文的理由", "fallback")]
    [InlineData("has spaces", "fallback")]
    [InlineData("-leading-dash", "fallback")]
    [InlineData("", "fallback")]
    public void Codes_that_would_be_scrubbed_by_the_gateway_fall_back_locally(
        string code,
        string expected)
    {
        Assert.Equal(expected, WmsLifecycleConflictMiddleware.SafeOutboundCode(code, expected));
    }

    /// <summary>
    /// 分类按 <see cref="ArgumentException.ParamName"/> 而不是消息文本——聚合改文案不该让分类失效。
    /// </summary>
    [Fact]
    public void Warehouse_task_argument_failures_are_classified_by_parameter_name()
    {
        Assert.Equal(
            WmsUnprocessableReasonCodes.PickingDifferenceReasonRequired,
            WmsUnprocessableReasonCodes.FromWarehouseTaskArgument(
                new ArgumentException("任意文案", "completionReason")));
        Assert.Equal(
            WmsUnprocessableReasonCodes.ExecutedQuantityOutOfRange,
            WmsUnprocessableReasonCodes.FromWarehouseTaskArgument(
                new ArgumentOutOfRangeException("executedQuantity", 5m, "任意文案")));
        Assert.Equal(
            WmsUnprocessableException.SafeCode,
            WmsUnprocessableReasonCodes.FromWarehouseTaskArgument(
                new ArgumentException("任意文案", "somethingElse")));
    }

    private static async Task<(int StatusCode, WmsLifecycleConflictResponse? Body)> InvokeAsync(
        Exception thrown)
    {
        using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new WmsLifecycleConflictMiddleware(
            _ => throw thrown,
            NullLogger<WmsLifecycleConflictMiddleware>.Instance);

        await middleware.InvokeAsync(context, dbContext);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<WmsLifecycleConflictResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return (context.Response.StatusCode, body);
    }
}
