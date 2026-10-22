using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using NetCorePal.Extensions.Dto;
using NJsonSchema;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

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

/// <summary>
/// Keeps the shared response envelope accurate for both successful responses and
/// downstream proxy failures. Success and existing local failures retain their
/// numeric HTTP code; governed downstream failure envelopes may use a semantic
/// string code.
/// </summary>
public sealed class BusinessGatewayErrorResponseOpenApiDocumentProcessor : IDocumentProcessor
{
    private const string ResponseDataSchemaName = "NetCorePalExtensionsDtoResponseData";

    public void Process(DocumentProcessorContext context)
    {
        if (!context.Document.Components.Schemas.TryGetValue(ResponseDataSchemaName, out var schema))
        {
            throw new InvalidOperationException($"Missing shared response envelope schema: {ResponseDataSchemaName}");
        }

        if (!schema.Properties.TryGetValue("code", out var code))
        {
            throw new InvalidOperationException($"Missing shared response envelope code property: {ResponseDataSchemaName}.code");
        }

        code.Type = JsonObjectType.None;
        code.Format = null;
        code.OneOf.Clear();
        code.OneOf.Add(new JsonSchema { Type = JsonObjectType.Integer, Format = "int32" });
        code.OneOf.Add(new JsonSchema { Type = JsonObjectType.String });
    }
}
