using FluentValidation.Results;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web;

/// <summary>
/// 将 FastEndpoints DTO 和模型绑定错误写成网关失败信封。
/// </summary>
/// <remarks>
/// 前端用顶层稳定码查找用户文案。字段名及本地化校验原因保留在 errorData 中供定位。
/// </remarks>
public static class BusinessGatewayValidationErrorResponse
{
    /// <summary>
    /// 校验失败的稳定 wire 码。**改这个字符串就是改公开契约**：
    /// 前端 <c>frontend/packages/business-core/src/labels/stableErrorMessages.ts</c>
    /// 按这个精确值（不做归一化、不做前缀匹配）查表，改名而不同步那张表 ⇒ 操作工屏上回落成裸码。
    /// </summary>
    public const string StableErrorCode = "request-payload-invalid";

    /// <summary>
    /// <c>FastEndpoints.ErrorOptions.ResponseBuilder</c> 的实现。
    /// </summary>
    /// <remarks>
    /// <paramref name="statusCode"/> 由 <c>ErrorOptions.StatusCode</c> 给（默认 400），
    /// 原样放进信封的 <c>code</c> 位——与
    /// <see cref="ResponseDataEndpointResults.WriteErrorAsync(HttpContext, int, string, CancellationToken)"/>
    /// 的口径一致：本地失败用数字码，只有下游治理过的语义码才放字符串。
    /// </remarks>
    public static object Build(List<ValidationFailure> failures, HttpContext context, int statusCode)
    {
        ArgumentNullException.ThrowIfNull(failures);
        _ = context;
        return new ResponseData(
            false,
            StableErrorCode,
            statusCode,
            failures.Select(object (failure) => new ValidationErrorItem(
                failure.PropertyName,
                failure.ErrorMessage)).ToArray());
    }

    /// <summary>
    /// <c>errorData</c> 的条目形状。字段名与 FastEndpoints 默认 <c>errors{}</c> 的
    /// 「属性名 → 原因」两元组同构，只是摊平成数组（同一属性多条失败就是多个条目）。
    /// </summary>
    private sealed record ValidationErrorItem(string Name, string Reason);
}
