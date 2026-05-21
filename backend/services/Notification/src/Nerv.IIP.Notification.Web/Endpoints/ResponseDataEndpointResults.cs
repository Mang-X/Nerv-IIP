using System.Text.Json;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Notification.Web.Endpoints;

internal static class ResponseDataEndpointResults
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
}
