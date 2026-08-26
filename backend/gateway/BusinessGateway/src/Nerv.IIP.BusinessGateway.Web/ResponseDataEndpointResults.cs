using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web;

internal static class ResponseDataEndpointResults
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteDataAsync<T>(
        HttpContext context,
        int statusCode,
        T data,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            data.AsResponseData(),
            jsonOptions ?? JsonOptions,
            cancellationToken);
    }

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ResponseData(false, message, statusCode, []),
            JsonOptions,
            cancellationToken);
    }

    public static async Task WriteErrorAsync(
        HttpContext context,
        BusinessServiceProxyException exception,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = (int)exception.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new ProxyErrorResponseData(
                false,
                exception.Message,
                exception.SemanticCode is null
                    ? JsonSerializer.SerializeToElement((int)exception.StatusCode)
                    : JsonSerializer.SerializeToElement(exception.SemanticCode),
                exception.ErrorData),
            JsonOptions,
            cancellationToken);
    }

    private sealed record ProxyErrorResponseData(
        bool Success,
        string Message,
        JsonElement Code,
        IReadOnlyCollection<JsonElement> ErrorData);
}
