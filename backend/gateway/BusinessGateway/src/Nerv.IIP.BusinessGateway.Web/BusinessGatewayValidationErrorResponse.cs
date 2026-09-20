using FluentValidation.Results;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web;

/// <summary>
/// FastEndpoints 校验失败（DTO 校验器与模型绑定失败）的公开响应成形（#3333）。
/// </summary>
/// <remarks>
/// <para><b>它改掉的是什么</b>：FastEndpoints 默认把校验失败写成
/// <c>{"statusCode":400,"message":"One or more errors occurred!","errors":{…}}</c>。
/// 顶层 <c>message</c> 是一个<b>英文常量</b>，而两端的错误展示链都从顶层 <c>message</c>
/// 起读（PC <c>notify.ts</c> 的 <c>rawMessage</c>/<c>serverErrorMessage</c>、
/// PDA <c>request-timeout.ts</c> 的 <c>extractServerMessage</c>），
/// 于是操作工屏上出现的就是那句英文常量。</para>
///
/// <para><b>改成什么</b>：与本网关既有失败通道（<see cref="ResponseDataEndpointResults"/>
/// 写出的 <c>BusinessServiceProxyException</c> 信封、<c>UseKnownExceptionHandler</c>
/// 的 KnownException 信封、以及鉴权拒绝）<b>同一个</b>
/// <see cref="ResponseData"/> 信封 <c>{success,message,code,errorData}</c>，
/// <c>message</c> 位放<b>稳定错误码</b> <see cref="StableErrorCode"/>，
/// 由前端 <c>STABLE_ERROR_MESSAGES</c> 一处映射成中文。</para>
///
/// <para>⚠️ <b>本类不做字段级文案</b>：<c>errorData</c> 里逐条的
/// <see cref="ValidationFailure.ErrorMessage"/> 是 FluentValidation 按
/// <c>CurrentUICulture</c> 出的默认句子（本网关默认 <c>zh-CN</c>，形如
/// 「'idempotency Key' 必须小于或等于128个字符。您输入了129个字符。」，属性名仍是英文驼峰；
/// <c>Accept-Language: en-US</c> 时是英文）。这些句子<b>不上屏</b>——
/// 两端都只读顶层 <c>message</c>，没有任何前端位点消费 <c>errorData</c>。
/// ⇒ 用户看到的是<b>一条稳定码映射出的中文</b>，<b>不是</b>「具体哪个字段错了」。
/// 字段级中文文案是另一票的事，别把本类读成已经做到了。</para>
///
/// <para><b>为什么 <c>errorData</c> 仍要带上这些条目</b>：它们是本网关<b>今天就已经</b>
/// 经 <c>errors{}</c> 公开的同一份信息（值域逐字未变，只换了槽位），
/// 留着排障才不会因为「修了上屏文案」把开发侧的定位信息一起删掉。
/// <c>errorData</c> 在 <see cref="ResponseData"/> 里本就是 FluentValidation 的字段袋。</para>
///
/// <para><b>射程</b>：只挂在 BusinessGateway 上。PlatformGateway 没有任何
/// <c>Validator&lt;&gt;</c>，这条通道在那边不产出响应。</para>
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
