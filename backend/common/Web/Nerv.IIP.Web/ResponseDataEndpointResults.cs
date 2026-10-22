using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Web;

public static class ResponseDataEndpointResults
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteDataAsync<T>(
        HttpContext context,
        int statusCode,
        T data,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null) =>
        WriteAsync(
            context,
            statusCode,
            data.AsResponseData(),
            jsonOptions ?? DefaultJsonOptions,
            cancellationToken);

    public static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken,
        JsonSerializerOptions? jsonOptions = null) =>
        WriteAsync(
            context,
            statusCode,
            new ResponseData(false, message, statusCode, []),
            jsonOptions ?? DefaultJsonOptions,
            cancellationToken);

    private static async Task WriteAsync<T>(
        HttpContext context,
        int statusCode,
        T envelope,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            envelope,
            jsonOptions,
            cancellationToken);
    }
}
